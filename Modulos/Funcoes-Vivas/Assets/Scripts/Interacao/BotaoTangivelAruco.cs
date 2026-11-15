using System;
using UnityEngine;
using UnityEngine.Events;
using XRTangivel.Core;

public class BotaoTangivelAruco : MonoBehaviour, IInteracaoAcaoProvider
{
    [Header("Conexão")]
    [Tooltip("Arraste o objeto que tem o script RastreadorAruco aqui")]
    public RastreadorAruco rastreador;
    
    [Header("Configuração do Botão")]
    [Tooltip("O número do ArUco impresso no papel que servirá de botão")]
    public int idMarcador = 45;
    
    [Header("Regras do Clique (Ocultação)")]
    [Tooltip("Quanto tempo o marcador precisa estar visível (segundos) para 'armar' o botão")]
    public float tempoParaArmar = 0.3f;
    [Tooltip("Tempo máximo que o dedo pode tapar o marcador para ser considerado um clique (se passar disso, o clique é cancelado)")]
    public float tempoMaximoTampado = 1.0f;

    [Header("Eventos Unity (Inspector)")]
    [Tooltip("Dispara na hora que você põe o dedo em cima do marcador")]
    public UnityEvent AoApertar; 
    
    [Tooltip("Dispara quando você tira o dedo (o clique final e principal)")]
    public UnityEvent AoSoltar;  

    // Implementação de IInteracaoAcaoProvider
    public event Action OnAlternarModo;
    public event Action OnNovoDesafio;
    public event Action OnResetarPosicao;

    // Estado interno
    private bool visivelNoUltimoFrame = false;
    private float cronometroVisivel = 0f;
    private float cronometroOculto = 0f;
    private bool botaoArmado = false;
    private bool dedoEmCima = false;

    void Update()
    {
        if (rastreador == null)
        {
            rastreador = FindAnyObjectByType<RastreadorAruco>();
            if (rastreador == null) return;
        }

        // Verifica se o ID deste botão está na lista de marcadores que a câmera viu NESTE frame
        bool visivelAgora = rastreador.marcadoresVisiveisNesteFrame.Contains(idMarcador);

        if (visivelAgora)
        {
            cronometroOculto = 0f; // Zera o tempo que passou tapado

            if (!visivelNoUltimoFrame) 
            {
                // Acabou de aparecer (você tirou o dedo!)
                if (dedoEmCima)
                {
                    // Concluiu o clique físico!
                    AoSoltar?.Invoke();
                    OnNovoDesafio?.Invoke();
                    Debug.Log($"<color=cyan>[Botão Tangível {idMarcador}] CLIQUE REGISTRADO! (Dedo removido)</color>");
                    dedoEmCima = false;
                }
            }

            cronometroVisivel += Time.deltaTime;
            
            // Se você ficou olhando pra ele tempo suficiente, o botão está pronto pra ser apertado
            if (cronometroVisivel >= tempoParaArmar && !botaoArmado)
            {
                botaoArmado = true;
                Debug.Log($"<color=green>[Botão Tangível {idMarcador}] Armado e pronto para clique.</color>");
            }
        }
        else // Não está visível
        {
            cronometroVisivel = 0f;

            if (visivelNoUltimoFrame && botaoArmado)
            {
                // Acabou de sumir da câmera (você botou o dedo em cima)
                dedoEmCima = true;
                AoApertar?.Invoke();
                Debug.Log($"<color=yellow>[Botão Tangível {idMarcador}] Dedo em cima! (Aguardando soltar...)</color>");
            }

            if (dedoEmCima)
            {
                cronometroOculto += Time.deltaTime;
                
                // Se o marcador ficar tapado tempo demais (escondeu o papel de propósito ou tirou a câmera de cima)
                if (cronometroOculto > tempoMaximoTampado)
                {
                    botaoArmado = false;
                    dedoEmCima = false;
                    Debug.Log($"<color=red>[Botão Tangível {idMarcador}] Clique cancelado (ficou tapado/fora de cena por mais de {tempoMaximoTampado}s)</color>");
                }
            }
            else
            {
                botaoArmado = false;
            }
        }

        visivelNoUltimoFrame = visivelAgora;
    }
}
