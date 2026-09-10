using System;
using UnityEngine;
using TMPro;

public class VisualizadorFuncaoQuadratica : MonoBehaviour
{
    [Header("Coeficientes da Função f(x) = ax² + bx + c")]
    [Tooltip("Valor do escalar 'a' (curvatura / abertura da parábola)")]
    public float coeficienteA = 1.0f;

    [Tooltip("Valor do escalar 'b' (inclinação / deslocamento do vértice)")]
    public float coeficienteB = 0.0f;

    [Tooltip("Valor do escalar 'c' (intercepto no eixo Y)")]
    public float coeficienteC = 0.0f;

    [Header("Configurações da Curva")]
    [Tooltip("Número de segmentos da linha da parábola")]
    [Range(20, 200)]
    public int resolucao = 100;

    [Tooltip("Limite inferior de X")]
    public float xMin = -2.0f;

    [Tooltip("Limite superior de X")]
    public float xMax = 2.0f;

    [Tooltip("Escala multiplicadora de tamanho no mundo (em metros para AR)")]
    public float escalaGrafico = 0.25f;

    [Header("Estilo Visual")]
    public Color corParabola = new Color(0.2f, 0.9f, 1.0f, 1.0f); // Ciano brilhante
    public float espessuraLinha = 0.015f;

    [Header("Eixos Cartesianos (X e Y)")]
    public bool desenharEixos = true;
    public Color corEixos = new Color(1.0f, 1.0f, 1.0f, 0.45f);
    public float espessuraEixos = 0.008f;

    [Header("Posicionamento em Realidade Aumentada")]
    [Tooltip("Se ativado, mantém o gráfico flutuando na frente da câmera")]
    public bool seguirCamera = true;
    public float distanciaCamera = 1.0f;
    public Vector3 offsetCamera = new Vector3(0f, -0.1f, 0f);
    public float suavizacaoSeguir = 5.0f;

    [Header("Referências de Componentes (Opcional)")]
    public LineRenderer linhaParabola;
    public LineRenderer linhaEixos;
    public TMP_Text textoEquacao;

    private Camera camPrincipal;

    void Awake()
    {
        camPrincipal = Camera.main;
        ConfigurarComponentes();
    }

    void Start()
    {
        if (camPrincipal == null)
            camPrincipal = Camera.main;

        if (seguirCamera && camPrincipal != null)
        {
            Vector3 posAlvo = camPrincipal.transform.position + camPrincipal.transform.forward * distanciaCamera + camPrincipal.transform.TransformDirection(offsetCamera);
            transform.position = posAlvo;
            transform.rotation = Quaternion.LookRotation(transform.position - camPrincipal.transform.position);
        }

        RedesenharGrafico();
    }

    void LateUpdate()
    {
        if (seguirCamera && camPrincipal != null)
        {
            Vector3 posAlvo = camPrincipal.transform.position + camPrincipal.transform.forward * distanciaCamera + camPrincipal.transform.TransformDirection(offsetCamera);
            transform.position = Vector3.Lerp(transform.position, posAlvo, Time.deltaTime * suavizacaoSeguir);
            
            // Mantém virado para a câmera
            Vector3 direcao = transform.position - camPrincipal.transform.position;
            if (direcao.sqrMagnitude > 0.001f)
            {
                Quaternion rotAlvo = Quaternion.LookRotation(direcao);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotAlvo, Time.deltaTime * suavizacaoSeguir);
            }
        }
    }

    /// <summary>
    /// Cria e configura automaticamente os LineRenderers e o texto se não foram atribuídos no Inspector.
    /// </summary>
    public void ConfigurarComponentes()
    {
        Material matLinha = CriarMaterialUnlit();

        // 1. Linha da Parábola
        if (linhaParabola == null)
        {
            linhaParabola = GetComponent<LineRenderer>();
            if (linhaParabola == null)
            {
                GameObject objParabola = new GameObject("Linha_Parabola");
                objParabola.transform.SetParent(transform, false);
                linhaParabola = objParabola.AddComponent<LineRenderer>();
            }
        }

        linhaParabola.useWorldSpace = false;
        linhaParabola.material = matLinha;
        linhaParabola.startColor = corParabola;
        linhaParabola.endColor = corParabola;
        linhaParabola.startWidth = espessuraLinha;
        linhaParabola.endWidth = espessuraLinha;
        linhaParabola.positionCount = resolucao;

        // 2. Eixos Cartesianos
        if (desenharEixos)
        {
            if (linhaEixos == null)
            {
                Transform tEixos = transform.Find("Linha_Eixos");
                if (tEixos != null) linhaEixos = tEixos.GetComponent<LineRenderer>();

                if (linhaEixos == null)
                {
                    GameObject objEixos = new GameObject("Linha_Eixos");
                    objEixos.transform.SetParent(transform, false);
                    linhaEixos = objEixos.AddComponent<LineRenderer>();
                }
            }

            linhaEixos.useWorldSpace = false;
            linhaEixos.material = matLinha;
            linhaEixos.startColor = corEixos;
            linhaEixos.endColor = corEixos;
            linhaEixos.startWidth = espessuraEixos;
            linhaEixos.endWidth = espessuraEixos;
            linhaEixos.loop = false;

            DesenharEixosCartesianos();
        }

        // 3. Texto informativo da equação
        if (textoEquacao == null)
        {
            textoEquacao = GetComponentInChildren<TMP_Text>();
            if (textoEquacao == null)
            {
                GameObject objTexto = new GameObject("Texto_Equacao");
                objTexto.transform.SetParent(transform, false);
                objTexto.transform.localPosition = new Vector3(0f, (xMax * xMax * escalaGrafico) + 0.08f, 0f);

                textoEquacao = objTexto.AddComponent<TextMeshPro>();
                textoEquacao.fontSize = 1.6f;
                textoEquacao.alignment = TextAlignmentOptions.Center;
                textoEquacao.color = Color.white;
            }
        }
    }

    /// <summary>
    /// Atualiza todos os coeficientes e redesenha a curva.
    /// </summary>
    public void AtualizarCoeficientes(float novoA, float novoB, float novoC)
    {
        coeficienteA = novoA;
        coeficienteB = novoB;
        coeficienteC = novoC;
        RedesenharGrafico();
    }

    public void AtualizarCoeficienteA(float novoA)
    {
        coeficienteA = novoA;
        RedesenharGrafico();
    }

    public void AtualizarCoeficienteB(float novoB)
    {
        coeficienteB = novoB;
        RedesenharGrafico();
    }

    public void AtualizarCoeficienteC(float novoC)
    {
        coeficienteC = novoC;
        RedesenharGrafico();
    }

    /// <summary>
    /// Calcula os vértices da parábola f(x) = a * x^2 + b * x + c
    /// </summary>
    public void RedesenharGrafico()
    {
        if (linhaParabola == null) return;

        if (linhaParabola.positionCount != resolucao)
            linhaParabola.positionCount = resolucao;

        for (int i = 0; i < resolucao; i++)
        {
            float t = (float)i / (resolucao - 1);
            float x = Mathf.Lerp(xMin, xMax, t);
            float y = (coeficienteA * (x * x)) + (coeficienteB * x) + coeficienteC;

            // Aplica a escala visual para o mundo AR
            Vector3 pontoLocal = new Vector3(x * escalaGrafico, y * escalaGrafico, 0f);
            linhaParabola.SetPosition(i, pontoLocal);
        }

        // Atualiza o texto da fórmula
        if (textoEquacao != null)
        {
            string equacaoFormatada = FormatarEquacao();
            string detalhes = ObterDetalhesMatematicos();

            textoEquacao.text = $"{equacaoFormatada}\n<size=65%>{detalhes}</size>";

            // Ajusta a posição vertical do texto para ficar sempre visível logo acima do gráfico
            float topoY = Mathf.Max(
                (coeficienteA * xMin * xMin) + (coeficienteB * xMin) + coeficienteC,
                (coeficienteA * xMax * xMax) + (coeficienteB * xMax) + coeficienteC
            );
            float yTexto = (topoY * escalaGrafico) + 0.12f;
            textoEquacao.transform.localPosition = new Vector3(0f, Mathf.Clamp(yTexto, 0.2f, 1.5f), 0f);
        }
    }

    private string FormatarEquacao()
    {
        string sinalB = coeficienteB >= 0 ? "+" : "-";
        string sinalC = coeficienteC >= 0 ? "+" : "-";

        return $"f(x) = {coeficienteA:F2}x² {sinalB} {Mathf.Abs(coeficienteB):F2}x {sinalC} {Mathf.Abs(coeficienteC):F2}";
    }

    private string ObterDetalhesMatematicos()
    {
        if (Mathf.Abs(coeficienteA) < 0.001f)
        {
            return "Função Afim / Reta (a ≈ 0)";
        }

        // Vértice da parábola: Xv = -b / 2a, Yv = -Δ / 4a
        float xv = -coeficienteB / (2f * coeficienteA);
        float yv = (coeficienteA * xv * xv) + (coeficienteB * xv) + coeficienteC;

        // Discriminante: Δ = b² - 4ac
        float delta = (coeficienteB * coeficienteB) - (4f * coeficienteA * coeficienteC);

        string infoRaizes;
        if (delta > 0.001f)
            infoRaizes = "2 raízes reais";
        else if (Mathf.Abs(delta) <= 0.001f)
            infoRaizes = "1 raiz real (dupla)";
        else
            infoRaizes = "Sem raízes reais (Δ < 0)";

        return $"Vértice: ({xv:F2}, {yv:F2})  |  Δ = {delta:F2} ({infoRaizes})";
    }

    private void DesenharEixosCartesianos()
    {
        if (linhaEixos == null) return;

        // Desenha uma cruz contínua: (-X -> +X -> Origem -> -Y -> +Y)
        linhaEixos.positionCount = 5;
        float xDist = (xMax + 0.5f) * escalaGrafico;
        float yDist = (xMax + 0.5f) * escalaGrafico;

        linhaEixos.SetPosition(0, new Vector3(-xDist, 0f, 0f));
        linhaEixos.SetPosition(1, new Vector3(xDist, 0f, 0f));
        linhaEixos.SetPosition(2, new Vector3(0f, 0f, 0f));
        linhaEixos.SetPosition(3, new Vector3(0f, -yDist, 0f));
        linhaEixos.SetPosition(4, new Vector3(0f, yDist, 0f));
    }

    private Material CriarMaterialUnlit()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.color = Color.white;
        return mat;
    }
}
