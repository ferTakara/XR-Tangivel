using System;
using UnityEngine;

public class GeradorDesafioQuadratica : MonoBehaviour
{
    [Header("Referência ao Visualizador Atual")]
    public VisualizadorFuncaoQuadratica visualizador;

    [Header("Configuração dos Pontos Visuais")]
    public float margemAcerto = 0.2f;

    /// <summary>Disparado uma vez quando o aluno acerta os 3 pontos do desafio atual.</summary>
    public event Action OnDesafioResolvido;

    private float alvoA, alvoB, alvoC;
    private GameObject[] pontosNaTela = new GameObject[3];
    private Renderer[] renderersPontos = new Renderer[3];
    private bool[] pontoAcertado = new bool[3];
    private Vector2[] coordenadasAlvo = new Vector2[3];
    private bool desafioResolvido = false;

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
            
            renderersPontos[i] = pontosNaTela[i].GetComponent<Renderer>();
            pontoAcertado[i] = false;
            DefinirCor(renderersPontos[i], Color.yellow);
        }

        desafioResolvido = false;

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

    public void AtivarModo(bool ativo)
    {
        this.enabled = ativo;
        for (int i = 0; i < 3; i++)
        {
            if (pontosNaTela[i] != null)
            {
                pontosNaTela[i].SetActive(ativo);
            }
        }
        
        // Se ativou o modo e ainda não tinha gerado as bolinhas, gera agora
        if (ativo && pontosNaTela[0] == null) 
        {
            GerarNovoDesafio();
        }
    }

    void Update()
    {
        if (!this.enabled) return;
        VerificarVitoria();
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

            bool acertou = Mathf.Abs(yUsuario - yEsperado) <= margemAcerto;
            if (!acertou) ganhou = false;

            // Só troca a cor quando o estado do ponto muda (evita mexer no material todo frame)
            if (acertou != pontoAcertado[i])
            {
                pontoAcertado[i] = acertou;
                DefinirCor(renderersPontos[i], acertou ? Color.green : Color.yellow);
            }
        }

        // Avisa uma única vez por desafio (antes logava todo frame enquanto estava resolvido)
        if (ganhou && !desafioResolvido)
        {
            desafioResolvido = true;
            Debug.Log("<color=green>VOCÊ RESOLVEU O QUEBRA-CABEÇA!</color>");
            OnDesafioResolvido?.Invoke();
        }
        else if (!ganhou)
        {
            desafioResolvido = false;
        }
    }

    private float SorteioArredondado(float min, float max, float step)
    {
        int range = Mathf.RoundToInt((max - min) / step);
        int rand = UnityEngine.Random.Range(0, range + 1);
        return min + (rand * step);
    }
}