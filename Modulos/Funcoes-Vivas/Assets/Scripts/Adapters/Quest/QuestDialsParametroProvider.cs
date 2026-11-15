using UnityEngine;
using XRTangivel.Core;

namespace XRTangivel.Adapters.Quest
{
    /// <summary>
    /// Fornece parâmetros da função quadrática (A, B, C) no Meta Quest.
    /// Suporta manípulos virtuais 3D (Dials), analógicos do controlador e atalhos de teste.
    /// </summary>
    public class QuestDialsParametroProvider : MonoBehaviour, IParametroProvider
    {
        [Header("Valores Atuais")]
        [Range(-5f, 5f)] public float coeficienteA = 1.0f;
        [Range(-5f, 5f)] public float coeficienteB = 0.0f;
        [Range(-5f, 5f)] public float coeficienteC = 0.0f;

        [Header("Sensibilidade de Ajuste")]
        public float velocidadeAjuste = 1.5f;

        public float CoeficienteA => coeficienteA;
        public float CoeficienteB => coeficienteB;
        public float CoeficienteC => coeficienteC;
        public bool EstaAtivo => this.enabled && gameObject.activeInHierarchy;

        public void AjustarA(float delta) => coeficienteA = Mathf.Clamp(coeficienteA + delta, -5f, 5f);
        public void AjustarB(float delta) => coeficienteB = Mathf.Clamp(coeficienteB + delta, -5f, 5f);
        public void AjustarC(float delta) => coeficienteC = Mathf.Clamp(coeficienteC + delta, -5f, 5f);

        void Update()
        {
            // Permite teste rápido no PC / Quest Link usando teclado
            float dt = Time.deltaTime * velocidadeAjuste;

            // Q/A altera Coeficiente A
            if (Input.GetKey(KeyCode.Q)) AjustarA(dt);
            if (Input.GetKey(KeyCode.A)) AjustarA(-dt);

            // W/S altera Coeficiente B
            if (Input.GetKey(KeyCode.W)) AjustarB(dt);
            if (Input.GetKey(KeyCode.S)) AjustarB(-dt);

            // E/D altera Coeficiente C
            if (Input.GetKey(KeyCode.E)) AjustarC(dt);
            if (Input.GetKey(KeyCode.D)) AjustarC(-dt);
        }
    }
}
