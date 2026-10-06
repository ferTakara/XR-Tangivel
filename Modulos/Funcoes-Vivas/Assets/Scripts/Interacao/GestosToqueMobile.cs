using UnityEngine;

/// <summary>
/// Gerencia gestos de toque na tela do celular (Mobile AR):
/// - Pinça com 2 dedos: Zoom in / Zoom out no gráfico matemático.
/// - Arrastar com 2 dedos: Desloca o gráfico (Pan) para cima, baixo e lados.
/// - Toque duplo com 1 dedo: Reseta escala e posição para o padrão.
/// - Toque duplo com 2 dedos: Sorteia um novo desafio/quebra-cabeça.
/// Também funciona no Unity Editor com Scroll do Mouse e clique do botão do meio.
/// </summary>
public class GestosToqueMobile : MonoBehaviour
{
    [Header("Referências")]
    public VisualizadorFuncaoQuadratica visualizador;
    public GeradorDesafioQuadratica geradorDesafio;

    [Header("Sensibilidades")]
    [Tooltip("Velocidade do zoom ao fazer o movimento de pinça com 2 dedos")]
    public float sensibilidadeZoom = 0.003f;

    [Tooltip("Velocidade de deslocamento ao arrastar com 2 dedos")]
    public float sensibilidadePan = 0.001f;

    [Header("Limites de Escala")]
    public float escalaMinima = 0.4f;
    public float escalaMaxima = 3.0f;

    // Valores padrão para reset
    private Vector3 escalaInicial;
    private Vector3 offsetInicial;

    // Controle de toque duplo
    private float ultimoToqueTempo = 0f;
    private const float INTERVALO_TOQUE_DUPLO = 0.35f;

    void Start()
    {
        // Encontra os scripts automaticamente se não forem vinculados no Inspector
        if (visualizador == null)
            visualizador = GetComponent<VisualizadorFuncaoQuadratica>() ?? FindAnyObjectByType<VisualizadorFuncaoQuadratica>();

        if (geradorDesafio == null)
            geradorDesafio = GetComponent<GeradorDesafioQuadratica>() ?? FindAnyObjectByType<GeradorDesafioQuadratica>();

        if (visualizador != null)
        {
            escalaInicial = visualizador.transform.localScale;
            offsetInicial = visualizador.offsetCamera;
        }
        else
        {
            escalaInicial = Vector3.one;
            offsetInicial = Vector3.zero;
        }
    }

    void Update()
    {
        // 1. Processa toques na tela (Celular Android)
        if (Input.touchCount > 0)
        {
            ProcessarToquesMobile();
        }
        // 2. Processa mouse no Unity Editor (Para testes sem precisar compilar)
        else
        {
            ProcessarMouseEditor();
        }
    }

    private void ProcessarToquesMobile()
    {
        // GESTO COM 2 DEDOS (Zoom e Pan)
        if (Input.touchCount == 2)
        {
            Touch toque0 = Input.GetTouch(0);
            Touch toque1 = Input.GetTouch(1);

            // Detecção de Toque Duplo com 2 dedos -> Gera novo desafio
            if (toque0.phase == TouchPhase.Began && toque1.phase == TouchPhase.Began)
            {
                if (Time.time - ultimoToqueTempo < INTERVALO_TOQUE_DUPLO)
                {
                    if (geradorDesafio != null)
                    {
                        Debug.Log("<color=cyan>[GESTO] Toque duplo com 2 dedos: Gerando novo desafio!</color>");
                        geradorDesafio.GerarNovoDesafio();
                    }
                    ultimoToqueTempo = 0f;
                    return;
                }
                ultimoToqueTempo = Time.time;
            }

            // Posições dos dedos no frame anterior
            Vector2 toque0PosAnterior = toque0.position - toque0.deltaPosition;
            Vector2 toque1PosAnterior = toque1.position - toque1.deltaPosition;

            // Distâncias entre os dedos
            float distanciaAnterior = (toque0PosAnterior - toque1PosAnterior).magnitude;
            float distanciaAtual = (toque0.position - toque1.position).magnitude;
            float diferencaDistancia = distanciaAtual - distanciaAnterior;

            // A) PINÇA: Zoom In / Zoom Out
            if (Mathf.Abs(diferencaDistancia) > 1.5f && visualizador != null)
            {
                float fatorZoom = diferencaDistancia * sensibilidadeZoom;
                Vector3 novaEscala = visualizador.transform.localScale + (Vector3.one * fatorZoom);

                novaEscala.x = Mathf.Clamp(novaEscala.x, escalaMinima, escalaMaxima);
                novaEscala.y = Mathf.Clamp(novaEscala.y, escalaMinima, escalaMaxima);
                novaEscala.z = Mathf.Clamp(novaEscala.z, escalaMinima, escalaMaxima);

                visualizador.transform.localScale = novaEscala;
            }

            // B) PAN: Arrastar para reposicionar o gráfico no campo de visão
            Vector2 deltaMedio = (toque0.deltaPosition + toque1.deltaPosition) * 0.5f;
            if (deltaMedio.sqrMagnitude > 2f && visualizador != null)
            {
                visualizador.offsetCamera.x += deltaMedio.x * sensibilidadePan;
                visualizador.offsetCamera.y += deltaMedio.y * sensibilidadePan;
            }
        }
        // GESTO COM 1 DEDO (Toque Duplo para resetar)
        else if (Input.touchCount == 1)
        {
            Touch toque = Input.GetTouch(0);
            if (toque.phase == TouchPhase.Began)
            {
                if (Time.time - ultimoToqueTempo < INTERVALO_TOQUE_DUPLO)
                {
                    ResetarPosicaoEEscala();
                    ultimoToqueTempo = 0f;
                }
                else
                {
                    ultimoToqueTempo = Time.time;
                }
            }
        }
    }

    private void ProcessarMouseEditor()
    {
        if (visualizador == null) return;

        // Scroll do mouse = Zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float fatorZoom = scroll * 2.0f;
            Vector3 novaEscala = visualizador.transform.localScale + (Vector3.one * fatorZoom);

            novaEscala.x = Mathf.Clamp(novaEscala.x, escalaMinima, escalaMaxima);
            novaEscala.y = Mathf.Clamp(novaEscala.y, escalaMinima, escalaMaxima);
            novaEscala.z = Mathf.Clamp(novaEscala.z, escalaMinima, escalaMaxima);

            visualizador.transform.localScale = novaEscala;
        }

        // Botão direito ou do meio pressionado e arrastado = Pan
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            visualizador.offsetCamera.x += mouseX * 0.05f;
            visualizador.offsetCamera.y += mouseY * 0.05f;
        }

        // Pressionar tecla 'R' no editor = Reset
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetarPosicaoEEscala();
        }

        // Pressionar tecla 'N' no editor = Novo desafio
        if (Input.GetKeyDown(KeyCode.N) && geradorDesafio != null)
        {
            geradorDesafio.GerarNovoDesafio();
        }
    }

    /// <summary>
    /// Restaura o tamanho e a posição do gráfico para os padrões iniciais.
    /// </summary>
    public void ResetarPosicaoEEscala()
    {
        if (visualizador == null) return;

        visualizador.transform.localScale = escalaInicial;
        visualizador.offsetCamera = offsetInicial;
        Debug.Log("<color=green>[GESTO] Posição e Zoom resetados para o padrão!</color>");
    }
}
