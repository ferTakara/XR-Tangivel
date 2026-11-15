using UnityEngine;

namespace XRTangivel.Rastreamento
{
    /// <summary>
    /// Estabiliza a pose de algo que fica PARADO no mundo (ex.: o cartão-âncora na mesa).
    /// Pequenas variações são tratadas como ruído e viram uma média lenta.
    /// Uma mudança grande e persistente (vários frames seguidos) é tratada como movimento real
    /// e a pose salta para o valor novo.
    ///
    /// A posição é separada em duas partes em relação ao raio câmera -> objeto:
    /// lateral (onde o objeto aparece na imagem: medida estável) e profundidade (distância ao longo
    /// do raio: medida ruidosa). A profundidade tem média mais longa e um limiar de salto bem maior,
    /// para o ruído de distância não ser confundido com o cartão sendo movido.
    /// </summary>
    public class EstabilizadorPoseEstatica
    {
        /// <summary>Constante de tempo da média lateral e do yaw (s). Maior = mais estável, mais lento.</summary>
        public float tau = 0.5f;
        /// <summary>Constante de tempo da média da profundidade (s): mais longa, pois é a medida mais ruidosa.</summary>
        public float tauProfundidade = 1.5f;
        /// <summary>Deslocamento LATERAL acima disto (m) conta como "fora" da posição atual.</summary>
        public float limiarMovimentoMetros = 0.03f;
        /// <summary>Variação de PROFUNDIDADE acima disto (m) conta como "fora" (bem maior: ruído de distância).</summary>
        public float limiarProfundidadeMetros = 0.15f;
        /// <summary>Acima disto (graus) o yaw é "fora" do atual.</summary>
        public float limiarMovimentoGraus = 8f;
        /// <summary>Quantos frames "fora" seguidos para aceitar que o objeto foi movido.</summary>
        public int framesParaMover = 5;

        public Vector3 Posicao { get; private set; }
        public float YawGraus { get; private set; }
        public bool Valido { get; private set; }

        private int framesFora;
        private float ultimoTempo;

        /// <param name="direcaoRaio">Direção câmera -> objeto (normalizada) neste frame.</param>
        public void Atualizar(Vector3 posicao, float yawGraus, float tempo, Vector3 direcaoRaio)
        {
            if (!Valido)
            {
                Saltar(posicao, yawGraus, tempo);
                return;
            }

            float dt = Mathf.Clamp(tempo - ultimoTempo, 0.001f, 0.25f);
            ultimoTempo = tempo;

            Vector3 delta = posicao - Posicao;
            Vector3 deltaProfundidade = Vector3.Dot(delta, direcaoRaio) * direcaoRaio;
            Vector3 deltaLateral = delta - deltaProfundidade;

            bool fora = deltaLateral.magnitude > limiarMovimentoMetros ||
                        deltaProfundidade.magnitude > limiarProfundidadeMetros ||
                        Mathf.Abs(Mathf.DeltaAngle(YawGraus, yawGraus)) > limiarMovimentoGraus;

            if (fora)
            {
                // Pode ser um outlier isolado: só aceita depois de vários frames seguidos
                framesFora++;
                if (framesFora >= framesParaMover) Saltar(posicao, yawGraus, tempo);
                return;
            }

            framesFora = 0;
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(tau, 0.01f));
            float aProf = 1f - Mathf.Exp(-dt / Mathf.Max(tauProfundidade, 0.01f));
            Posicao += deltaLateral * a + deltaProfundidade * aProf;
            YawGraus += Mathf.DeltaAngle(YawGraus, yawGraus) * a;
        }

        public void Reset()
        {
            Valido = false;
            framesFora = 0;
        }

        private void Saltar(Vector3 posicao, float yawGraus, float tempo)
        {
            Posicao = posicao;
            YawGraus = yawGraus;
            ultimoTempo = tempo;
            framesFora = 0;
            Valido = true;
        }
    }
}
