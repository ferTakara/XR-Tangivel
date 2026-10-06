using System;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Rastreia o esqueleto da mÃƒÂ£o (21 pontos 3D) em tempo real no celular usando Unity Sentis (Inference Engine)
/// e o modelo MediaPipe BlazeHand.
/// Desenha o esqueleto anatÃƒÂ´mico da mÃƒÂ£o sobreposto ÃƒÂ  cÃƒÂ¢mera com material Unlit brilhante.
/// </summary>
public class RastreadorMaoAR : MonoBehaviour
{
    [Header("ConexÃƒÂ£o com AR")]
    public ARCameraManager cameraManager;
    public ARCameraBackground cameraBackground;
    public Camera cameraAR;

    [Header("ConfiguraÃƒÂ§ÃƒÂµes do Modelo AI")]
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
    public float distanciaProfundidadeCamera = 0.45f; // 45 cm ÃƒÂ  frente da cÃƒÂ¢mera

    [Header("CalibraÃ§Ã£o de Alinhamento (Eixos da CÃ¢mera)")]
    [Tooltip("Inverte o eixo X (esquerda/direita). Mantenha desmarcado por padrÃ£o.")]
    public bool inverterEixoX = false;

    [Tooltip("Inverte o eixo Y (cima/baixo). Mantenha desmarcado para nÃ£o ficar de cabeÃ§a para baixo.")]
    public bool inverterEixoY = false;

    [Tooltip("Troca os eixos X e Y (caso o sensor do celular esteja em 90 graus)")]
    public bool trocarXeY = false;

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

    // Textura da cÃƒÂ¢mera e processamento
    private RenderTexture renderTextureCamera;
    private bool estaProcessando = false;
    private bool modelosCarregados = false;

    // Elementos visuais gerados automaticamente
    private GameObject containerEsqueleto;
    private Transform[] esferasArticulacoes = new Transform[NUM_KEYPOINTS];
    private LineRenderer[] linhasOssos;
    private Vector3[] posicoes3D = new Vector3[NUM_KEYPOINTS];
    private bool maoDetectada = false;

    // DefiniÃƒÂ§ÃƒÂ£o das conexÃƒÂµes anatÃƒÂ´micas dos ossos da mÃƒÂ£o (Pares de ÃƒÂ­ndices de 0 a 20)
    private readonly int[][] conexoesOssos = new int[][]
    {
        // Polegar
        new int[] { 0, 1 }, new int[] { 1, 2 }, new int[] { 2, 3 }, new int[] { 3, 4 },
        // Indicador
        new int[] { 0, 5 }, new int[] { 5, 6 }, new int[] { 6, 7 }, new int[] { 7, 8 },
        // MÃƒÂ©dio
        new int[] { 0, 9 }, new int[] { 9, 10 }, new int[] { 10, 11 }, new int[] { 11, 12 },
        // Anelar
        new int[] { 0, 13 }, new int[] { 13, 14 }, new int[] { 14, 15 }, new int[] { 15, 16 },
        // Mindinho
        new int[] { 0, 17 }, new int[] { 17, 18 }, new int[] { 18, 19 }, new int[] { 19, 20 },
        // Palma (articulaÃƒÂ§ÃƒÂµes da base dos dedos)
        new int[] { 5, 9 }, new int[] { 9, 13 }, new int[] { 13, 17 }
    };

    void Awake()
    {
        if (cameraAR == null) cameraAR = Camera.main;
        if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
        if (cameraBackground == null) cameraBackground = FindAnyObjectByType<ARCameraBackground>();

        AutoCarregarAssets();
        CriarEstruturaVisualEsqueleto();
    }

    void Start()
    {
        InicializarSentis();
    }

    void OnEnable()
    {
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

        // Garante que o RenderTexture existe com tamanho adequado
        if (renderTextureCamera == null)
        {
            renderTextureCamera = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32);
            renderTextureCamera.Create();
        }

        // Copia a imagem da cÃƒÂ¢mera AR para o RenderTexture usando o material de fundo do AR
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

            // Matriz de transformaÃƒÂ§ÃƒÂ£o afim para o detector (192x192)
            float scale = size / (float)DETECTOR_INPUT_SIZE;
            var M = BlazeUtils.mul(
                BlazeUtils.TranslationMatrix(0.5f * (new Vector2(texWidth, texHeight) + new Vector2(-size, size))),
                BlazeUtils.ScaleMatrix(new Vector2(scale, -scale))
            );

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
            float2 boxCentre = anchorPosition + new float2(outputBox[0, 0, 0], outputBox[0, 0, 1]);
            float boxSize = math.max(outputBox[0, 0, 2], outputBox[0, 0, 3]);

            float2 kp0 = anchorPosition + new float2(outputBox[0, 0, 4], outputBox[0, 0, 5]);
            float2 kp2 = anchorPosition + new float2(outputBox[0, 0, 8], outputBox[0, 0, 9]);
            float2 delta = kp2 - kp0;
            float2 up = delta / math.length(delta);
            float theta = math.atan2(delta.y, delta.x);
            float rotation = 0.5f * Mathf.PI - theta;

            boxCentre += 0.5f * boxSize * up;
            boxSize *= 2.6f;

            // Recorta a regiÃƒÂ£o da palma para o detector de landmarks (224x224)
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

            // Converte os 21 pontos para o espaÃƒÂ§o 3D do mundo real ÃƒÂ  frente da cÃƒÂ¢mera AR
            if (cameraAR != null)
            {
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
                        distanciaProfundidadeCamera + profRelativa
                    ));

                    posicoes3D[i] = posMundo;
                    esferasArticulacoes[i].position = posMundo;
                }

                // Atualiza as conexÃƒÂµes dos ossos
                for (int i = 0; i < conexoesOssos.Length; i++)
                {
                    int idxStart = conexoesOssos[i][0];
                    int idxEnd = conexoesOssos[i][1];
                    linhasOssos[i].SetPosition(0, posicoes3D[idxStart]);
                    linhasOssos[i].SetPosition(1, posicoes3D[idxEnd]);
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
