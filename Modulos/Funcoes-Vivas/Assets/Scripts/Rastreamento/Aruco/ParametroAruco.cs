using UnityEngine;

namespace XRTangivel.Rastreamento
{
    public enum ModoRotacaoAruco
    {
        PotenciometroContinuo, // Acumula giros como um botão giratório físico (permite várias voltas)
        AnguloAbsoluto         // O ângulo do marcador define o valor diretamente (-180° a +180°)
    }

    /// <summary>
    /// Um coeficiente (a, b ou c) controlado pelo giro de um marcador ArUco.
    /// Antes era uma classe aninhada no RastreadorAruco; os nomes dos campos foram mantidos
    /// para a cena continuar com os valores já configurados.
    /// </summary>
    [System.Serializable]
    public class ParametroAruco
    {
        public string nome = "Parâmetro";
        public int idAlvo = 42;
        public float valorAtual = 0f;
        public float sensibilidade = 0.01f;
        public bool limitarValor = true;
        public float valorMin = -5.0f;
        public float valorMax = 5.0f;

        [HideInInspector] public float valorPotenciometro = 0f;
        [HideInInspector] public float ultimoAngulo = 0f;
        [HideInInspector] public bool primeiraVez = true;

        private float SensibilidadeSegura => Mathf.Abs(sensibilidade) > 0.0001f ? sensibilidade : 0.01f;

        public void Inicializar(float valorInicial)
        {
            valorAtual = valorInicial;
            valorPotenciometro = valorAtual / SensibilidadeSegura;
            primeiraVez = true;
        }

        public void ProcessarRotacao(float anguloAtual, ModoRotacaoAruco modo)
        {
            if (modo == ModoRotacaoAruco.PotenciometroContinuo)
            {
                if (primeiraVez)
                {
                    ultimoAngulo = anguloAtual;
                    primeiraVez = false;
                }

                float giro = Mathf.DeltaAngle(ultimoAngulo, anguloAtual);
                valorPotenciometro += giro;
                ultimoAngulo = anguloAtual;
                valorAtual = valorPotenciometro * SensibilidadeSegura;
            }
            else // AnguloAbsoluto
            {
                valorAtual = anguloAtual * SensibilidadeSegura;
            }

            if (limitarValor)
            {
                valorAtual = Mathf.Clamp(valorAtual, valorMin, valorMax);
                if (modo == ModoRotacaoAruco.PotenciometroContinuo)
                    valorPotenciometro = valorAtual / SensibilidadeSegura;
            }
        }

        /// <summary>Chamado quando o marcador some, para o próximo giro não "pular".</summary>
        public void ResetarGiro()
        {
            primeiraVez = true;
        }
    }
}
