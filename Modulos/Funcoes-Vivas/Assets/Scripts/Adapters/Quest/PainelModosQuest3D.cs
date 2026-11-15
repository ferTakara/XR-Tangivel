using UnityEngine;
using XRTangivel.Core;

namespace XRTangivel.Adapters.Quest
{
    /// <summary>
    /// Menu 3D no espaço do mundo (World-Space) ou anexado ao pulso do aluno para o Meta Quest.
    /// Só repassa os comandos ao ControladorSessao (modo e desafios) e à âncora da mesa.
    /// </summary>
    public class PainelModosQuest3D : MonoBehaviour
    {
        [Header("Conexões")]
        public QuestMesaAncoraProvider ancoraQuest;

        [Header("Estado do Modo (somente leitura)")]
        public bool modoDesafio = false;

        private ControladorSessao sessao;

        void Start()
        {
            if (ancoraQuest == null)
                ancoraQuest = FindAnyObjectByType<QuestMesaAncoraProvider>();

            sessao = ControladorSessao.Obter();
            sessao.OnModoAlterado += AoMudarModo;
            modoDesafio = sessao.ModoDesafio;
        }

        void OnDestroy()
        {
            if (sessao != null) sessao.OnModoAlterado -= AoMudarModo;
        }

        private void AoMudarModo(bool desafio) => modoDesafio = desafio;

        public void AlternarModo() => ControladorSessao.Obter().AlternarModo();
        public void SetModo(bool desafio) => ControladorSessao.Obter().SetModo(desafio);
        public void PedirNovoDesafio() => ControladorSessao.Obter().PedirNovoDesafio();

        public void AlternarAncoraMesa()
        {
            if (ancoraQuest == null) return;
            if (ancoraQuest.EstaAncorado) ancoraQuest.Desancorar();
            else ancoraQuest.FixarNaMesa();
        }

        void Update()
        {
            // Atalho para testes no Editor / Quest Link ('N' fica no GestosToqueMobile)
            if (Input.GetKeyDown(KeyCode.M)) AlternarModo();
        }
    }
}
