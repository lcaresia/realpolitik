using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace CurrencyMod.Diplomacia.Viewer
{
    /// <summary>Pedido vindo do navegador (formulário de debug), tratado na thread principal.</summary>
    internal sealed class ViewerRequest
    {
        public string Kind;
        public string Body;
    }

    /// <summary>
    /// Servidor HTTP mínimo só para esta máquina (127.0.0.1), para o visualizador de debug (F10).
    /// TcpListener em vez de HttpListener: não depende de nada do sistema e funciona igual no Mono da Unity.
    /// A thread do servidor só lê textos prontos (trocados de forma atômica pela thread principal).
    /// </summary>
    internal sealed class ViewerServer
    {
        private readonly int port;
        private readonly string token = Guid.NewGuid().ToString("N");
        private TcpListener listener;
        private Thread thread;
        private volatile bool running;
        private string page;

        internal volatile string StateJson = "{}";
        internal readonly ConcurrentDictionary<int, string> DetailJson = new ConcurrentDictionary<int, string>();
        internal readonly ConcurrentQueue<ViewerRequest> Requests = new ConcurrentQueue<ViewerRequest>();
        internal string LastError;

        internal ViewerServer(int port)
        {
            this.port = port;
        }

        internal string Url => $"http://localhost:{port}/";
        internal bool IsRunning => running;

        internal bool Start()
        {
            try
            {
                page = LoadPage().Replace("__TOKEN__", token);
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                running = true;
                thread = new Thread(AcceptLoop) { IsBackground = true, Name = "DiplomaciaIA.Viewer" };
                thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                running = false;
                try
                {
                    listener?.Stop();
                }
                catch (Exception)
                {
                }
                return false;
            }
        }

        internal void Stop()
        {
            running = false;
            try
            {
                listener?.Stop();
            }
            catch (Exception)
            {
            }
            listener = null;
        }

        private static string LoadPage()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("viewer.html"))
            {
                if (stream == null)
                {
                    return "<html><body>viewer.html não foi embutido no mod.</body></html>";
                }
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (!running)
                    {
                        return;
                    }
                    Thread.Sleep(200);
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    if (!TryReadRequest(stream, out string method, out string path, out string host, out string origin, out string requestToken, out string body))
                    {
                        return;
                    }
                    // Só atende quem chamou por localhost (bloqueia DNS rebinding) e, em POST, a própria página.
                    if (!IsLocalHost(host))
                    {
                        Write(stream, 403, "text/plain", "forbidden");
                        return;
                    }
                    if (method == "POST" && (requestToken != token || (origin != null && !IsLocalOrigin(origin))))
                    {
                        Write(stream, 403, "text/plain", "forbidden");
                        return;
                    }
                    Route(stream, method, path, body);
                }
                catch (Exception)
                {
                    // Navegador fechou a conexão no meio: ignora.
                }
            }
        }

        private void Route(NetworkStream stream, string method, string path, string body)
        {
            string route = path;
            string query = string.Empty;
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                route = path.Substring(0, q);
                query = path.Substring(q + 1);
            }

            if (method == "GET" && (route == "/" || route == "/index.html"))
            {
                Write(stream, 200, "text/html; charset=utf-8", page);
            }
            else if (method == "GET" && route == "/api/state")
            {
                Write(stream, 200, "application/json; charset=utf-8", StateJson);
            }
            else if (method == "GET" && route == "/api/detail")
            {
                int empire = -1;
                foreach (string pair in query.Split('&'))
                {
                    if (pair.StartsWith("e=") && int.TryParse(pair.Substring(2), out int parsed))
                    {
                        empire = parsed;
                    }
                }
                Write(stream, 200, "application/json; charset=utf-8", DetailJson.TryGetValue(empire, out string detail) ? detail : "{}");
            }
            else if (method == "POST" && route.StartsWith("/api/"))
            {
                Requests.Enqueue(new ViewerRequest { Kind = route.Substring(5), Body = body });
                Write(stream, 200, "application/json; charset=utf-8", "{\"ok\":true}");
            }
            else if (route == "/favicon.ico")
            {
                Write(stream, 204, "text/plain", string.Empty);
            }
            else
            {
                Write(stream, 404, "text/plain", "not found");
            }
        }

        private bool IsLocalHost(string host) => host == $"localhost:{port}" || host == $"127.0.0.1:{port}";

        private bool IsLocalOrigin(string origin) => origin == $"http://localhost:{port}" || origin == $"http://127.0.0.1:{port}";

        private static bool TryReadRequest(NetworkStream stream, out string method, out string path, out string host, out string origin, out string requestToken, out string body)
        {
            method = path = host = origin = requestToken = body = null;
            var headerBytes = new MemoryStream();
            int matched = 0;
            // Lê até o fim dos cabeçalhos (\r\n\r\n), com limite de tamanho.
            while (headerBytes.Length < 32 * 1024)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    return false;
                }
                headerBytes.WriteByte((byte)b);
                matched = (b == '\r' && (matched == 0 || matched == 2)) || (b == '\n' && (matched == 1 || matched == 3)) ? matched + 1 : (b == '\r' ? 1 : 0);
                if (matched == 4)
                {
                    break;
                }
            }
            if (matched != 4)
            {
                return false;
            }
            string[] lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return false;
            }
            method = requestLine[0].ToUpperInvariant();
            path = requestLine[1];
            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string name = lines[i].Substring(0, colon).Trim().ToLowerInvariant();
                string value = lines[i].Substring(colon + 1).Trim();
                switch (name)
                {
                    case "host": host = value; break;
                    case "origin": origin = value; break;
                    case "x-token": requestToken = value; break;
                    case "content-length": int.TryParse(value, out contentLength); break;
                }
            }
            if (contentLength > 0 && contentLength < 256 * 1024)
            {
                byte[] buffer = new byte[contentLength];
                int read = 0;
                while (read < contentLength)
                {
                    int n = stream.Read(buffer, read, contentLength - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
                body = Encoding.UTF8.GetString(buffer, 0, read);
            }
            return true;
        }

        private static void Write(NetworkStream stream, int status, string contentType, string content)
        {
            byte[] payload = Encoding.UTF8.GetBytes(content ?? string.Empty);
            string reason = status == 200 ? "OK" : status == 204 ? "No Content" : status == 403 ? "Forbidden" : "Not Found";
            string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\n"
                + "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }
}
