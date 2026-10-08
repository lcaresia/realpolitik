using System.Linq;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using Amplitude.UI.Interactables;
using Amplitude.Mercury.UI;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>Utilitários comuns para montar UI com peças clonadas do jogo.</summary>
    internal static class NativeUIKit
    {
        /// <summary>
        /// Clona uma peça (de qualquer janela) sob um pai inativo, remove scripts que tentariam se
        /// ligar à janela original e coloca o clone no destino, visível.
        /// </summary>
        public static Transform Clone(Transform donor, Transform parent, string name, params string[] stripTypes)
        {
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            try
            {
                GameObject clone = Object.Instantiate(donor.gameObject, stash.transform);
                clone.name = name;
                foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && stripTypes.Contains(behaviour.GetType().Name))
                    {
                        Object.DestroyImmediate(behaviour);
                    }
                }
                clone.transform.SetParent(parent, false);
                UITransform ui = clone.GetComponent<UITransform>();
                if (ui != null)
                {
                    ui.VisibleSelf = true;
                    ui.InteractiveSelf = true;
                }
                return clone.transform;
            }
            finally
            {
                Object.Destroy(stash);
            }
        }

        /// <summary>Solta todas as âncoras e posiciona pela borda superior esquerda, relativa ao pai.</summary>
        public static void Place(Transform target, float left, float top, float width, float height)
        {
            UITransform ui = target.GetComponent<UITransform>();
            ui.LeftAnchor = ui.LeftAnchor.SetAttach(false);
            ui.RightAnchor = ui.RightAnchor.SetAttach(false);
            ui.TopAnchor = ui.TopAnchor.SetAttach(false);
            ui.BottomAnchor = ui.BottomAnchor.SetAttach(false);
            ui.PivotXAnchor = ui.PivotXAnchor.SetAttach(false);
            ui.PivotYAnchor = ui.PivotYAnchor.SetAttach(false);
            ui.Pivot = Vector2.zero;
            ui.Width = width;
            ui.Height = height;
            ui.X = left;
            ui.Y = top;
        }

        /// <summary>
        /// Barra de comparação clonada da diplomacia: o preenchimento (Mask/Value) vem na cor do estado de moral da tela de origem
        /// (muda conforme a sessão: carmim, rosa...) e o separador fica no meio. Cor explícita e sem separador.
        /// </summary>
        public static void StyleGauge(Transform valueGroup, Color fill)
        {
            if (valueGroup == null)
            {
                return;
            }
            Transform value = valueGroup.Find("Mask/Value");
            UISquircleImage image = value != null ? value.GetComponent<UISquircleImage>() : null;
            if (image != null && image.Color != fill)
            {
                image.Color = fill;
            }
            Transform separator = valueGroup.Find("Separator");
            if (separator != null)
            {
                NativeBankWindow.SetVisible(separator, false);
            }
            // A marca de variação (DeltaGroup) fica no meio da barra, com 6 px: é o "antes e depois" da moral na diplomacia.
            Transform delta = valueGroup.parent != null ? valueGroup.parent.Find("DeltaGroup") : null;
            if (delta != null)
            {
                NativeBankWindow.SetVisible(delta, false);
            }
        }

        private static readonly System.Collections.Generic.Dictionary<UITextField, object> hookedFields = new System.Collections.Generic.Dictionary<UITextField, object>();

        /// <summary>
        /// O campo cria o responder (quem dispara TextChange) ao carregar, depois de clonado: a assinatura feita antes se perde.
        /// Chame a cada quadro; religa o evento sempre que o responder muda.
        /// </summary>
        public static void EnsureTextChange(UITextField field, System.Action<IUITextField, string> handler)
        {
            object responder = field != null ? field.TextFieldResponder : null;
            if (responder == null)
            {
                return;
            }
            if (hookedFields.TryGetValue(field, out object current) && ReferenceEquals(current, responder))
            {
                return;
            }
            field.TextChange -= handler;
            field.TextChange += handler;
            hookedFields[field] = responder;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
            }
        }

        public static UILabel Label(Transform target) => target != null ? target.GetComponent<UILabel>() : null;

        public static void SetText(Transform target, string text)
        {
            UILabel label = Label(target);
            if (label != null && label.Text != text)
            {
                label.Text = text;
            }
        }

        public static void Align(Transform target, HorizontalAlignment horizontal)
        {
            UILabel label = Label(target);
            if (label != null)
            {
                label.Alignment = new Alignment(horizontal, VerticalAlignment.Center);
            }
        }

        public static void SetVisible(Transform target, bool visible)
        {
            UITransform ui = target != null ? target.GetComponent<UITransform>() : null;
            if (ui != null && ui.VisibleSelf != visible)
            {
                ui.VisibleSelf = visible;
            }
        }

        /// <summary>
        /// Ajuda ao passar o mouse num elemento qualquer. Se a peça não tem UITooltip (rótulos, barras),
        /// o componente nativo é adicionado; o texto usa o balão padrão do jogo (título + descrição).
        /// </summary>
        public static void Tip(Transform target, string title, string description)
        {
            if (target == null)
            {
                return;
            }
            if (target.GetComponent<UITooltip>() == null)
            {
                target.gameObject.AddComponent<UITooltip>();
            }
            NativeBankWindow.Tip(target, string.Empty, title, description);
        }

        private const string BadgeDonor = "AllSettlementsWindow/_SettlementsList/Scrollview/Viewport/SettlementItemsTable/_SettlementItemSample/Table/Top/StatsTable/PopCount";

        /// <summary>
        /// Selo no canto de um botão da barra (cartas novas, alerta): um chip de cartão nativo, avermelhado, escondido
        /// até ganhar texto. Null se o doador (lista de cidades do jogo) ainda não existir.
        /// </summary>
        public static Transform CreateBadge(Transform parent, string name)
        {
            Transform donor = DevTools.FindByPath(BadgeDonor);
            if (donor == null)
            {
                return null;
            }
            Transform chip = Clone(donor, parent, name);
            Transform picto = chip.Find("Picto");
            if (picto != null)
            {
                Object.DestroyImmediate(picto.gameObject);
            }
            UILabel label = chip.GetComponent<UILabel>();
            label.Margins = new RectMargins(5f, 5f, 0f, 0f);
            label.FontSize = 13;
            label.AutoAdjustWidth = true;
            label.Alignment = new Alignment(HorizontalAlignment.Center, VerticalAlignment.Center);
            UISquircleImage background = chip.GetComponent<UISquircleImage>();
            if (background != null)
            {
                background.Color = new Color(0.72f, 0.20f, 0.16f, 0.95f);
            }
            Place(chip, 32f, -6f, 22f, 20f);
            SetVisible(chip, false);
            return chip;
        }

        /// <summary>Mostra o selo com o texto, ou esconde se o texto for vazio.</summary>
        public static void SetBadge(Transform badge, string text)
        {
            if (badge == null)
            {
                return;
            }
            SetVisible(badge, !string.IsNullOrEmpty(text));
            UILabel label = Label(badge);
            if (label != null && !string.IsNullOrEmpty(text))
            {
                label.Text = text;
                label.AdjustSizesIfNecessary();
            }
        }

        /// <summary>
        /// Campo de texto que segura o teclado inteiro enquanto o jogador digita, como os da partida (renomear cidade,
        /// chat, busca de tecnologias: KeyboardOnly). Os campos clonados de telas de fora da partida (anotações do save)
        /// só seguravam os caracteres, e as teclas físicas disparavam os atalhos do jogo no meio do texto.
        /// </summary>
        public static void BlockGameShortcuts(UITextField field)
        {
            if (field != null)
            {
                field.blockedEvents |= UIEventBlockingMask.KeyboardOnly;
            }
        }

        /// <summary>
        /// O jogador está digitando num campo de texto. Os atalhos do mod (F8, F10) leem o teclado direto da Unity e
        /// não passam pelo bloqueio do campo, então conferem isto antes.
        /// </summary>
        public static bool TypingInField()
        {
            return UIInteractivityManager.Instance?.FocusedResponder is UITextFieldResponder;
        }

        /// <summary>
        /// Solta o foco de um campo de texto ao esconder a janela dele. Escondido, o campo continua segurando o teclado
        /// (KeyboardOnly) e engole o ESC e os atalhos do jogo até o próximo clique.
        /// </summary>
        public static void ReleaseTextFocus()
        {
            if (TypingInField())
            {
                UIInteractivityManager.Instance.SetFocus();
            }
        }

        /// <summary>Desmonta um objeto de UI com segurança (descarrega antes de destruir).</summary>
        public static void Dispose(Transform target)
        {
            if (target != null)
            {
                target.gameObject.SetActive(false);
                Object.Destroy(target.gameObject);
            }
        }
    }
}
