using UnityEngine;
using XRTangivel.Core;

namespace XRTangivel.Adapters.Quest
{
    /// <summary>
    /// Provedor de Âncora para Meta Quest (MR Passthrough).
    /// Permite fixar o gráfico matematicamente sobre a mesa real do usuário
    /// usando raycast de profundidade, controladores ou posição do headset.
    /// </summary>
    public class QuestMesaAncoraProvider : MonoBehaviour, IAncoraMundoProvider
    {
        [Header("Configurações da Âncora no Quest")]
        [Tooltip("Altura padrão da mesa relativa ao chão se não houver detecção de plano (ex: 0.75m)")]
        public float alturaMesaPadrao = 0.75f;

        [Tooltip("Distância à frente do usuário para criar a âncora ao pressionar o botão de fixação")]
        public float distanciaPadrao = 1.0f;

        [Header("Estado")]
        [SerializeField] private bool ancorado = false;
        private Vector3 posicaoFixa;
        private Quaternion rotacaoFixa;

        public bool EstaAncorado => ancorado;

        public bool TentarObterPose(out Vector3 posicao, out Quaternion rotacao)
        {
            posicao = posicaoFixa;
            rotacao = rotacaoFixa;
            return ancorado;
        }

        public void Desancorar()
        {
            ancorado = false;
            Debug.Log("[Quest Ancora] Gráfico desancorado. Retornando ao modo egocêntrico (flutuando).");
        }

        /// <summary>
        /// Fixa o gráfico na posição informada ou à frente do usuário na altura da mesa.
        /// </summary>
        public void FixarNaMesa(Vector3? posicaoCustomizada = null, Quaternion? rotacaoCustomizada = null)
        {
            if (posicaoCustomizada.HasValue)
            {
                posicaoFixa = posicaoCustomizada.Value;
                rotacaoFixa = rotacaoCustomizada ?? Quaternion.identity;
            }
            else
            {
                // Fixa a 1 metro à frente do headset na altura estimada da mesa
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 forwardPlano = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    posicaoFixa = cam.transform.position + forwardPlano * distanciaPadrao;
                    posicaoFixa.y = alturaMesaPadrao;

                    // Rotaciona para ficar de frente para o aluno
                    rotacaoFixa = Quaternion.LookRotation(forwardPlano, Vector3.up);
                }
            }

            ancorado = true;
            Debug.Log($"<color=cyan>[Quest Ancora] Gráfico fixado na mesa com sucesso em {posicaoFixa}!</color>");
        }

        void Update()
        {
            // Atalho no teclado / Editor para simular fixação ou desancoragem no Quest Link
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (!ancorado) FixarNaMesa();
                else Desancorar();
            }
        }
    }
}
