using System;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Rastreia o esqueleto da mÃ£o (21 pontos 3D) em tempo real no celular usando Unity Sentis (Inference Engine)
/// e o modelo MediaPipe BlazeHand.
/// Inclui pipeline otimizado com reconhecimento do gesto de Pinch (pinÃ§a entre polegar e indicador)
/// para translaÃ§Ã£o 3D completa do grÃ¡fico (esquerda/direita, cima/baixo e profundidade Z aproximar/afastar).
/// A profundidade Ã© estimada opticamente pelo tamanho da palma na imagem da cÃ¢mera.
/// </summary>
public class RastreadorMaoAR : MonoBehaviour
{
    [Header("AtivaÃ§Ã£o do MÃ³dulo")]
    [Tooltip("Marque para reativar o rastreador de mÃ£o quando desejar. EstÃ¡ desativado por padrÃ£o para economizar 100% de performance.")]
    public bool rastreadorHabilitado = false;

    [Header("ConexÃ£o com AR")]
    public ARCameraManager cameraManager;
    public ARCameraBackground cameraBackground;
    public Camera cameraAR;

    [Header("ConfiguraÃ§Ãµes do Modelo AI")]
    public ModelAsset handDetectorAsset;
    public ModelAsset handLandmarkerAsset;
    public TextAsset anchorsCSVAsset;
    [Range(0.1f, 0.9f)]
    public float pontuacaoMinima = 0.5f;

    [Header("Estilo Visual do Esqueleto")]
    public Color corArticulacoes = new Color(0.0f, 1.0f, 0.8f, 1.0f); // Ciano neon
    public Color corOssos = new Color(0.2f, 0.7f, 1.0f, 0.85f);       // Azul brilhante
    public float tamanhoArticulacao = 0.012f; // ~1.2 cm
    public float espessuraOsso = 0.005f;      // ~5 mm
    public float distanciaProfundidadeCamera = 0.45f; // 45 cm Ã  frente da cÃ¢mera

    [Header("InteraÃ§Ã£o Gestual (Pinch para TranslaÃ§Ã£o 3D do GrÃ¡fico)")]
    public VisualizadorFuncaoQuadratica visualizadorFuncao;
    [Tooltip("DistÃ¢ncia mÃ¡xima entre o polegar e o indicador para ativar o pinch")]
    public float distanciaPinchAtivacao = 0.045f; // 4.5 cm
    [Tooltip("DistÃ¢ncia para soltar o pinch (com folga para nÃ£o soltar acidentalmente)")]
    public float distanciaPinchSoltura = 0.06f;    // 6 cm
    public float sensibilidadeArrastoXY = 1.2f;
    [Tooltip("Sensibilidade da profundidade Z ao empurrar ou puxar a mÃ£o")]
    public float sensibilidadeProfundidadeZ = 0.02f;
    [Tooltip("Tamanho base da palma em pixels a 45cm para calibrar profundidade Ã³ptica")]
    public float tamanhoBasePalma = 80.0f;
    public Color corPinchAtivo = new Color(1.0f, 0.85f, 0.1f, 1.0f); // Dourado neon ao pinÃ§ar

    [Header("CalibraÃ§Ã£o de Alinhamento (Eixos da CÃ¢mera)")]
    [Tooltip("Inverte o eixo X (esquerda/direita). Mantenha desmarcado por padrÃ£o.")]
    public bool inverterEixoX = false;

    [Tooltip("Inverte o eixo Y (cima/baixo). Mantenha desmarcado para nÃ£o ficar de cabeÃ§a para baixo.")]
    public bool inverterEixoY = false;

    [Tooltip("Troca os eixos X e Y (caso o sensor do celular esteja em 90 graus)")]
    public bool trocarXeY = false;

    [Header("Desempenho e Taxa de AtualizaÃ§Ã£o (FPS)")]
    [Tooltip("ResoluÃ§Ã£o interna da textura para IA (256 Ã© 4x mais leve na GPU do celular que 512 sem perder precisÃ£o)")]
    public int resolucaoTexturaIA = 256;

    [Tooltip("Velocidade de acompanhamento dos ossos (30 a 50 garante resposta quase instantÃ¢nea)")]
    public float velocidadeResposta = 40.0f;

    [Tooltip("Pular detector de palma em frames alternados quando a mÃ£o jÃ¡ estiver rastreada (dobra o FPS da IA!)")]
    [Range(1, 4)]
    public int intervaloDeteccaoPalma = 2;

    // Constantes do BlazeHand
    const int NUM_ANCHORS = 2016;
    const int NUM_KEYPOINTS = 21;
    const int DETECTOR_INPUT_SIZE = 192;
    const int LANDMARKER_INPUT_SIZE = 224;

    // Sentis
    private Worker detectorWorker;
    private Worker landmarkerWorker;
    private Tensor<float> detectorInputTensor;
    private Tensor<float> landmarkerInputTensor;
    private float[,] anchorsData;

    // Textura da cÃ¢mera e processamento
    private RenderTexture renderTextureCamera;
    private bool estaProcessando = false;
    private bool modelosCarregados = false;
    private int contadorFrames = 0;
    private float2 ultimoBoxCentre;
    private float ultimoBoxSize;
    private float ultimoRotation;

    // Elementos visuais gerados automaticamente
    private GameObject containerEsqueleto;
    private Transform[] esferasArticulacoes = new Transform[NUM_KEYPOINTS];
    private LineRenderer[] linhasOssos;
    private Vector3[] posicoes3D = new Vector3[NUM_KEYPOINTS];
    private Vector3[] posicoesAlvo3D = new Vector3[NUM_KEYPOINTS];
    private bool maoDetectada = false;

    // Estado do Pinch e Profundidade
    private bool estaPinchando = false;
    private Vector3 posicaoPinchAnterior;
    private float spanPalmaPixelsAtual = 80.0f;
    private float spanPalmaAnterior = 80.0f;

    // DefiniÃ§Ã£o das conexÃµes anatÃ´micas dos ossos da mÃ£o (Pares de Ã­ndices de 0 a 20)
    private readonly int[][] conexoesOssos = new int[][]
    {
        // Polegar
        new int[] { 0, 1 }, new int[] { 1, 2 }, new int[] { 2, 3 }, new int[] { 3, 4 },
        // Indicador
        new int[] { 0, 5 }, new int[] { 5, 6 }, new int[] { 6, 7 }, new int[] { 7, 8 },
        // MÃ©dio
        new int[] { 0, 9 }, new int[] { 9, 10 }, new int[] { 10, 11 }, new int[] { 11, 12 },
        // Anelar
        new int[] { 0, 13 }, new int[] { 13, 14 }, new int[] { 14, 15 }, new int[] { 15, 16 },
        // Mindinho
        new int[] { 0, 17 }, new int[] { 17, 18 }, new int[] { 18, 19 }, new int[] { 19, 20 },
        // Palma (articulaÃ§Ãµes da base dos dedos)
        new int[] { 5, 9 }, new int[] { 9, 13 }, new int[] { 13, 17 }
    };

    void Awake()
    {
        if (!rastreadorHabilitado)
        {
            enabled = false;
            return;
        }

        if (cameraAR == null) cameraAR = Camera.main;
        if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
        if (cameraBackground == null) cameraBackground = FindAnyObjectByType<ARCameraBackground>();
        if (visualizadorFuncao == null) visualizadorFuncao = FindAnyObjectByType<VisualizadorFuncaoQuadratica>();

        AutoCarregarAssets();
        CriarEstruturaVisualEsqueleto();
    }

    void Start()
    {
        if (!rastreadorHabilitado) return;
        InicializarSentis();
    }

    void Update()
    {
        if (!maoDetectada)
        {
            if (estaPinchando) SoltarPinch();
            return;
        }

        // 1. InterpolaÃ§Ã£o suave a 60 FPS
        float t = Mathf.Clamp01(Time.deltaTime * velocidadeResposta);
        for (int i = 0; i < NUM_KEYPOINTS; i++)
        {
            esferasArticulacoes[i].position = Vector3.Lerp(esferasArticulacoes[i].position, posicoesAlvo3D[i], t);
            posicoes3D[i] = esferasArticulacoes[i].position;
        }

        for (int i = 0; i < conexoesOssos.Length; i++)
        {
            int idxStart = conexoesOssos[i][0];
            int idxEnd = conexoesOssos[i][1];
            linhasOssos[i].SetPosition(0, posicoes3D[idxStart]);
            linhasOssos[i].SetPosition(1, posicoes3D[idxEnd]);
        }

        // 2. Reconhecimento de gesto de Pinch para translaÃ§Ã£o 3D completa (X, Y e profundidade Z)
        ProcessarGestoPinch();
    }

    private void ProcessarGestoPinch()
    {
        if (visualizadorFuncao == null || !maoDetectada)
        {
            if (estaPinchando) SoltarPinch();
            return;
        }

        Vector3 pontaPolegar = posicoes3D[4];
        Vector3 pontaIndicador = posicoes3D[8];
        float distanciaDedos = Vector3.Distance(pontaPolegar, pontaIndicador);
        Vector3 centroPinch = (pontaPolegar + pontaIndicador) * 0.5f;

        if (!estaPinchando)
        {
            if (distanciaDedos < distanciaPinchAtivacao)
            {
                estaPinchando = true;
                posicaoPinchAnterior = centroPinch;
                spanPalmaAnterior = spanPalmaPixelsAtual;
                AtualizarCorPinch(true);
            }
        }
        else
        {
            if (distanciaDedos > distanciaPinchSoltura)
            {
                SoltarPinch();
            }
            else
            {
                // A) TranslaÃ§Ã£o nos eixos X e Y pelo movimento da mÃ£o
                Vector3 deltaMundo = centroPinch - posicaoPinchAnterior;
                posicaoPinchAnterior = centroPinch;

                if (cameraAR != null)
                {
                    Vector3 deltaCamera = cameraAR.transform.InverseTransformDirection(deltaMundo);
                    visualizadorFuncao.offsetCamera.x += deltaCamera.x * sensibilidadeArrastoXY;
                    visualizadorFuncao.offsetCamera.y += deltaCamera.y * sensibilidadeArrastoXY;
                }

                // B) TranslaÃ§Ã£o no eixo Z (Profundidade) pela variaÃ§Ã£o Ã³ptica do tamanho da mÃ£o
                // Se o span aumentou: mÃ£o veio mais perto da cÃ¢mera -> aproxima o grÃ¡fico
                // Se o span diminuiu: mÃ£o foi empurrada para longe da cÃ¢mera -> afasta o grÃ¡fico
                float deltaSpan = spanPalmaPixelsAtual - spanPalmaAnterior;
                spanPalmaAnterior = spanPalmaPixelsAtual;

                visualizadorFuncao.distanciaCamera = Mathf.Clamp(
                    visualizadorFuncao.distanciaCamera - (deltaSpan * sensibilidadeProfundidadeZ),
                    0.30f,
                    4.5f
                );
            }
        }
    }

    private void SoltarPinch()
    {
        estaPinchando = false;
        AtualizarCorPinch(false);
    }

    private void AtualizarCorPinch(bool ativo)
    {
        Color cor = ativo ? corPinchAtivo : corArticulacoes;
        if (esferasArticulacoes[4] != null)
        {
            var rend4 = esferasArticulacoes[4].GetComponent<Renderer>();
            if (rend4 != null) rend4.material.color = cor;
        }
        if (esferasArticulacoes[8] != null)
        {
            var rend8 = esferasArticulacoes[8].GetComponent<Renderer>();
            if (rend8 != null) rend8.material.color = cor;
        }
    }

    void OnEnable()
    {
        if (!rastreadorHabilitado) return;
        if (cameraManager != null)
            cameraManager.frameReceived += OnCameraFrameReceived;
    }

    void OnDisable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived -= OnCameraFrameReceived;
    }

    void OnDestroy()
    {
        detectorWorker?.Dispose();
        landmarkerWorker?.Dispose();
        detectorInputTensor?.Dispose();
        landmarkerInputTensor?.Dispose();
        if (renderTextureCamera != null)
            renderTextureCamera.Release();
    }

    private void AutoCarregarAssets()
    {
        if (handDetectorAsset == null)
            handDetectorAsset = Resources.Load<ModelAsset>("Models/hand_detector");
        if (handLandmarkerAsset == null)
            handLandmarkerAsset = Resources.Load<ModelAsset>("Models/hand_landmarks_detector");
        if (anchorsCSVAsset == null)
            anchorsCSVAsset = Resources.Load<TextAsset>("Data/anchors");
    }

    private void InicializarSentis()
    {
        if (handDetectorAsset == null || handLandmarkerAsset == null || anchorsCSVAsset == null)
        {
            Debug.LogError("[RastreadorMaoAR] Erro: Faltam arquivos do modelo (hand_detector, hand_landmarks_detector ou anchors) na pasta Resources!");
            return;
        }

        try
        {
            anchorsData = BlazeUtils.LoadAnchors(anchorsCSVAsset.text, NUM_ANCHORS);

            // Carrega e compila o detector de palma com filtro ArgMax
            var detectorModel = ModelLoader.Load(handDetectorAsset);
            var graph = new FunctionalGraph();
            var input = graph.AddInput(detectorModel, 0);
            var outputs = Functional.Forward(detectorModel, input);
            var boxes = outputs[0];
            var scores = outputs[1];
            var idx_scores_boxes = BlazeUtils.ArgMaxFiltering(boxes, scores);
            detectorModel = graph.Compile(idx_scores_boxes.Item1, idx_scores_boxes.Item2, idx_scores_boxes.Item3);

            detectorWorker = new Worker(detectorModel, BackendType.GPUCompute);

            // Carrega o detector de landmarks dos 21 pontos
            var landmarkerModel = ModelLoader.Load(handLandmarkerAsset);
            landmarkerWorker = new Worker(landmarkerModel, BackendType.GPUCompute);

            detectorInputTensor = new Tensor<float>(new TensorShape(1, DETECTOR_INPUT_SIZE, DETECTOR_INPUT_SIZE, 3));
            landmarkerInputTensor = new Tensor<float>(new TensorShape(1, LANDMARKER_INPUT_SIZE, LANDMARKER_INPUT_SIZE, 3));

            modelosCarregados = true;
            Debug.Log("<color=green>[RastreadorMaoAR] Unity Sentis inicializado com sucesso na GPU!</color>");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RastreadorMaoAR] Falha ao inicializar modelos: {ex.Message}");
        }
    }

    private void CriarEstruturaVisualEsqueleto()
    {
        containerEsqueleto = new GameObject("Esqueleto_Mao_Container");
        containerEsqueleto.transform.SetParent(transform, false);

        Material matArticulacao = CriarMaterialUnlit(corArticulacoes);
        Material matOsso = CriarMaterialUnlit(corOssos);

        // 1. Cria as 21 esferas (juntas)
        for (int i = 0; i < NUM_KEYPOINTS; i++)
        {
            GameObject objArticulacao = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            objArticulacao.name = $"Articulacao_{i}";
            objArticulacao.transform.SetParent(containerEsqueleto.transform, false);
            objArticulacao.transform.localScale = Vector3.one * tamanhoArticulacao;

            var col = objArticulacao.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = objArticulacao.GetComponent<Renderer>();
            if (rend != null) rend.material = matArticulacao;

            esferasArticulacoes[i] = objArticulacao.transform;
            posicoesAlvo3D[i] = Vector3.zero;
        }

        // 2. Cria as linhas conectando os ossos
        linhasOssos = new LineRenderer[conexoesOssos.Length];
        for (int i = 0; i < conexoesOssos.Length; i++)
        {
            GameObject objOsso = new GameObject($"Osso_{conexoesOssos[i][0]}_{conexoesOssos[i][1]}");
            objOsso.transform.SetParent(containerEsqueleto.transform, false);

            LineRenderer lr = objOsso.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = matOsso;
            lr.startColor = corOssos;
            lr.endColor = corOssos;
            lr.startWidth = espessuraOsso;
            lr.endWidth = espessuraOsso;
            lr.positionCount = 2;

            linhasOssos[i] = lr;
        }

        containerEsqueleto.SetActive(false);
    }

    private Material CriarMaterialUnlit(Color cor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                     ?? Shader.Find("Sprites/Default") 
                     ?? Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", cor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", cor);
        mat.color = cor;
        return mat;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!modelosCarregados || estaProcessando) return;

        // Garante que o RenderTexture existe com tamanho otimizado para a IA
        if (renderTextureCamera == null || renderTextureCamera.width != resolucaoTexturaIA)
        {
            if (renderTextureCamera != null) renderTextureCamera.Release();
            renderTextureCamera = new RenderTexture(resolucaoTexturaIA, resolucaoTexturaIA, 0, RenderTextureFormat.ARGB32);
            renderTextureCamera.Create();
        }

        // Copia a imagem da cÃ¢mera AR para o RenderTexture usando o material de fundo do AR
        if (cameraBackground != null && cameraBackground.material != null)
        {
            Graphics.Blit(null, renderTextureCamera, cameraBackground.material);
            ProcessarInferenciaAsync(renderTextureCamera);
        }
    }

    private async void ProcessarInferenciaAsync(Texture texture)
    {
        estaProcessando = true;

        try
        {
            int texWidth = texture.width;
            int texHeight = texture.height;
            float size = Mathf.Max(texWidth, texHeight);

            // Matriz de transformaÃ§Ã£o afim para o detector (192x192)
            float scale = size / (float)DETECTOR_INPUT_SIZE;
            var M = BlazeUtils.mul(
                BlazeUtils.TranslationMatrix(0.5f * (new Vector2(texWidth, texHeight) + new Vector2(-size, size))),
                BlazeUtils.ScaleMatrix(new Vector2(scale, -scale))
            );

            // Decide se roda o detector de palma (mais pesado) ou aproveita o rastreamento anterior
            bool precisaDetectarPalma = !maoDetectada || (contadorFrames % intervaloDeteccaoPalma == 0);
            contadorFrames++;

            float2 boxCentre;
            float boxSize;
            float rotation;

            if (precisaDetectarPalma)
            {
                BlazeUtils.SampleImageAffine(texture, detectorInputTensor, M);
                detectorWorker.Schedule(detectorInputTensor);

                var awaitIdx = (detectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
                var awaitScore = (detectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
                var awaitBox = (detectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();

                using var outputIdx = await awaitIdx;
                using var outputScore = await awaitScore;
                using var outputBox = await awaitBox;

                if (outputScore[0] < pontuacaoMinima)
                {
                    maoDetectada = false;
                    containerEsqueleto.SetActive(false);
                    return;
                }

                int idx = outputIdx[0];
                float2 anchorPosition = DETECTOR_INPUT_SIZE * new float2(anchorsData[idx, 0], anchorsData[idx, 1]);
                boxCentre = anchorPosition + new float2(outputBox[0, 0, 0], outputBox[0, 0, 1]);
                boxSize = math.max(outputBox[0, 0, 2], outputBox[0, 0, 3]);

                float2 kp0 = anchorPosition + new float2(outputBox[0, 0, 4], outputBox[0, 0, 5]);
                float2 kp2 = anchorPosition + new float2(outputBox[0, 0, 8], outputBox[0, 0, 9]);
                float2 delta = kp2 - kp0;
                float2 up = delta / math.length(delta);
                float theta = math.atan2(delta.y, delta.x);
                rotation = 0.5f * Mathf.PI - theta;

                boxCentre += 0.5f * boxSize * up;
                boxSize *= 2.6f;

                ultimoBoxCentre = boxCentre;
                ultimoBoxSize = boxSize;
                ultimoRotation = rotation;
            }
            else
            {
                boxCentre = ultimoBoxCentre;
                boxSize = ultimoBoxSize;
                rotation = ultimoRotation;
            }

            // Recorta a regiÃ£o da palma para o detector de landmarks (224x224)
            float2 origin2 = new float2(0.5f * LANDMARKER_INPUT_SIZE, 0.5f * LANDMARKER_INPUT_SIZE);
            float scale2 = boxSize / LANDMARKER_INPUT_SIZE;
            var M2 = BlazeUtils.mul(M, BlazeUtils.mul(BlazeUtils.mul(BlazeUtils.mul(
                BlazeUtils.TranslationMatrix(boxCentre),
                BlazeUtils.ScaleMatrix(new float2(scale2, -scale2))),
                BlazeUtils.RotationMatrix(rotation)),
                BlazeUtils.TranslationMatrix(-origin2)));

            BlazeUtils.SampleImageAffine(texture, landmarkerInputTensor, M2);
            landmarkerWorker.Schedule(landmarkerInputTensor);

            var tensorLandmarks = (landmarkerWorker.PeekOutput("Identity") ?? landmarkerWorker.PeekOutput(0)) as Tensor<float>;
            var awaitLandmarks = tensorLandmarks.ReadbackAndCloneAsync();
            using var landmarks = await awaitLandmarks;

            // Calcula a distÃ¢ncia fÃ­sica estimada da mÃ£o baseada no tamanho da palma em pixels
            float2 posImg0 = BlazeUtils.mul(M2, new float2(landmarks[3 * 0 + 0], landmarks[3 * 0 + 1])); // Pulso
            float2 posImg9 = BlazeUtils.mul(M2, new float2(landmarks[3 * 9 + 0], landmarks[3 * 9 + 1])); // Base do dedo mÃ©dio
            spanPalmaPixelsAtual = math.length(posImg9 - posImg0);

            // Profundidade da mÃ£o estimada opticamente (Ã³ptica de perspectiva pinhole)
            float profundidadeMaoEstimada = Mathf.Clamp(
                distanciaProfundidadeCamera * (tamanhoBasePalma / Mathf.Max(spanPalmaPixelsAtual, 15.0f)),
                0.20f,
                1.6f
            );

            // Converte os 21 pontos para o espaÃ§o 3D do mundo real Ã  frente da cÃ¢mera AR
            if (cameraAR != null)
            {
                bool primeiraVez = !containerEsqueleto.activeSelf;

                for (int i = 0; i < NUM_KEYPOINTS; i++)
                {
                    float2 posImg = BlazeUtils.mul(M2, new float2(landmarks[3 * i + 0], landmarks[3 * i + 1]));

                    float normX = posImg.x / (float)texWidth;
                    float normY = posImg.y / (float)texHeight;

                    float vx = inverterEixoX ? (1.0f - normX) : normX;
                    float vy = inverterEixoY ? (1.0f - normY) : normY;

                    if (trocarXeY)
                    {
                        float temp = vx;
                        vx = vy;
                        vy = temp;
                    }

                    float viewportX = vx;
                    float viewportY = vy;
                    float profRelativa = (landmarks[3 * i + 2] / (float)texHeight) * 0.25f;

                    Vector3 posMundo = cameraAR.ViewportToWorldPoint(new Vector3(
                        viewportX,
                        viewportY,
                        profundidadeMaoEstimada + profRelativa
                    ));

                    posicoesAlvo3D[i] = posMundo;

                    if (primeiraVez)
                    {
                        posicoes3D[i] = posMundo;
                        esferasArticulacoes[i].position = posMundo;
                    }
                }

                if (primeiraVez)
                {
                    for (int i = 0; i < conexoesOssos.Length; i++)
                    {
                        int idxStart = conexoesOssos[i][0];
                        int idxEnd = conexoesOssos[i][1];
                        linhasOssos[i].SetPosition(0, posicoes3D[idxStart]);
                        linhasOssos[i].SetPosition(1, posicoes3D[idxEnd]);
                    }
                }

                maoDetectada = true;
                containerEsqueleto.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RastreadorMaoAR] Erro no frame: {ex.Message}");
        }
        finally
        {
            estaProcessando = false;
        }
    }
}