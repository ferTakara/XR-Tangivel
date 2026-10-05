using UnityEngine;

public class GeradorDesafioQuadratica : MonoBehaviour
{
    [Header("Referência ao Visualizador Atual")]
    public VisualizadorFuncaoQuadratica visualizador;

    [Header("Configuração dos Pontos Visuais")]
    public float margemAcerto = 0.2f;

    private float alvoA, alvoB, alvoC;
    private GameObject[] pontosNaTela = new GameObject[3];
    private Vector2[] coordenadasAlvo = new Vector2[3];

    void Start()
    {
        // Tenta achar o visualizador no próprio objeto se a caixinha estiver vazia
        if (visualizador == null)
            visualizador = GetComponent<VisualizadorFuncaoQuadratica>();

        GerarNovoDesafio();
    }

    public void GerarNovoDesafio()
    {
        if (visualizador == null)
        {
            Debug.LogError("ERRO: O Gerador precisa do VisualizadorFuncaoQuadratica para saber onde criar os pontos!");
            return;
        }

        alvoA = SorteioArredondado(-2f, 2f, 0.5f);
        if (alvoA == 0f) alvoA = 1f;
        alvoB = SorteioArredondado(-2f, 2f, 0.5f);
        alvoC = SorteioArredondado(-1f, 2f, 0.5f);

        float[] valoresX = new float[] { -1.5f, 0f, 1.5f };

        // Pega o mesmo material que o visualizador já usa (garantido de funcionar no celular Android!)
        Material matBase = null;
        if (visualizador.linhaParabola != null && visualizador.linhaParabola.sharedMaterial != null)
        {
            matBase = visualizador.linhaParabola.sharedMaterial;
        }

        for (int i = 0; i < 3; i++)
        {
            float x = valoresX[i];
            float y = (alvoA * x * x) + (alvoB * x) + alvoC;
            coordenadasAlvo[i] = new Vector2(x, y);

            if (pontosNaTela[i] == null)
            {
                // Cria a esfera 3D nativa
                pontosNaTela[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                
                // Remove o collider para não atrapalhar física/toques
                Collider col = pontosNaTela[i].GetComponent<Collider>();
                if (col != null) Destroy(col);

                // Prende diretamente como filho do visualizador
                pontosNaTela[i].transform.SetParent(visualizador.transform, false);

                // Aplica o material seguro (o mesmo que a parábola usa)
                Renderer rend = pontosNaTela[i].GetComponent<Renderer>();
                if (rend != null)
                {
                    if (matBase != null)
                    {
                        rend.material = new Material(matBase);
                    }
                    DefinirCor(rend, Color.yellow);
                }
            }

            // Define posição relativa ao gráfico e escala visível
            pontosNaTela[i].transform.localPosition = new Vector3(x * visualizador.escalaGrafico, y * visualizador.escalaGrafico, 0f);
            pontosNaTela[i].transform.localScale = Vector3.one * 0.08f;
            
            Renderer r = pontosNaTela[i].GetComponent<Renderer>();
            if (r != null) DefinirCor(r, Color.yellow);
        }

        Debug.Log($"<color=yellow>Novo desafio gerado! Alvo: A={alvoA}, B={alvoB}, C={alvoC}</color>");
    }

    private void DefinirCor(Renderer rend, Color cor)
    {
        if (rend == null || rend.material == null) return;
        if (rend.material.HasProperty("_BaseColor"))
            rend.material.SetColor("_BaseColor", cor); // URP
        if (rend.material.HasProperty("_Color"))
            rend.material.SetColor("_Color", cor);     // Standard/Unlit
        rend.material.color = cor;
    }

    private float timerDebug = 0f;

    void Update()
    {
        VerificarVitoria();
        
        // Debug a cada 3 segundos
        timerDebug += Time.deltaTime;
        if (timerDebug > 3f)
        {
            timerDebug = 0f;
            if (pontosNaTela[0] != null)
            {
                Debug.Log($"[DEBUG PONTOS] Ponto 0 Posicao Local: {pontosNaTela[0].transform.localPosition} | Posicao Mundo: {pontosNaTela[0].transform.position} | Escala: {pontosNaTela[0].transform.lossyScale} | Camera: {Camera.main.transform.position}");
            }
        }
    }

    private void VerificarVitoria()
    {
        if (visualizador == null || pontosNaTela[0] == null) return;

        bool ganhou = true;

        for (int i = 0; i < 3; i++)
        {
            float x = coordenadasAlvo[i].x;
            float yEsperado = coordenadasAlvo[i].y;

            float yUsuario = (visualizador.coeficienteA * x * x) +
                             (visualizador.coeficienteB * x) +
                             visualizador.coeficienteC;

            Renderer rend = pontosNaTela[i].GetComponent<Renderer>();
            if (Mathf.Abs(yUsuario - yEsperado) > margemAcerto)
            {
                ganhou = false;
                DefinirCor(rend, Color.yellow);
            }
            else
            {
                DefinirCor(rend, Color.green);
            }
        }

        if (ganhou)
        {
            Debug.Log("<color=green>VOCÊ RESOLVEU O QUEBRA-CABEÇA!</color>");
        }
    }

    private float SorteioArredondado(float min, float max, float step)
    {
        int range = Mathf.RoundToInt((max - min) / step);
        int rand = Random.Range(0, range + 1);
        return min + (rand * step);
    }
}