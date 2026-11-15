using System;
using UnityEngine;
using TMPro;
using XRTangivel.Core;

namespace XRTangivel.Adapters.Quest
{
    /// <summary>
    /// Menu 3D no espaço do mundo (World-Space) ou anexado ao pulso do aluno para o Meta Quest.
    /// Permite alternar entre Sandbox e Desafio, gerar novos enigmas e fixar/desancorar da mesa.
    /// </summary>
    public class PainelModosQuest3D : MonoBehaviour, IInteracaoAcaoProvider
    {
        [Header("Conexões Core")]
        public GeradorDesafioQuadratica geradorDesafio;
        public QuestMesaAncoraProvider ancoraQuest;

        [Header("Estado do Modo")]
        public bool modoDesafio = false;

        public event Action OnAlternarModo;
        public event Action OnNovoDesafio;
        public event Action OnResetarPosicao;

        void Start()
        {
            if (geradorDesafio == null)
                geradorDesafio = FindAnyObjectByType<GeradorDesafioQuadratica>();

            if (ancoraQuest == null)
                ancoraQuest = FindAnyObjectByType<QuestMesaAncoraProvider>();

            SetModo(false);
        }

        public void AlternarModo()
        {
            SetModo(!modoDesafio);
        }

        public void SetModo(bool desafio)
        {
            modoDesafio = desafio;
            if (geradorDesafio != null)
            {
                geradorDesafio.AtivarModo(desafio);
            }
            OnAlternarModo?.Invoke();
            Debug.Log($"[Quest UI 3D] Modo alterado para: {(modoDesafio ? "Desafios" : "Livre (Sandbox)")}");
        }

        public void PedirNovoDesafio()
        {
            if (geradorDesafio != null)
            {
                geradorDesafio.GerarNovoDesafio();
                OnNovoDesafio?.Invoke();
                Debug.Log("[Quest UI 3D] Novo desafio gerado!");
            }
        }

        public void AlternarAncoraMesa()
        {
            if (ancoraQuest != null)
            {
                if (ancoraQuest.EstaAncorado)
                {
                    ancoraQuest.Desancorar();
                }
                else
                {
                    ancoraQuest.FixarNaMesa();
                }
                OnResetarPosicao?.Invoke();
            }
        }

        void Update()
        {
            // Atalhos rápidos no teclado para testes no Editor / Link
            if (Input.GetKeyDown(KeyCode.M)) AlternarModo();
            if (Input.GetKeyDown(KeyCode.N)) PedirNovoDesafio();
        }
    }
}
