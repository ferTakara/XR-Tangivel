using System;
using UnityEngine;
using XRTangivel.Core;

public class GerenciadorModosXR : MonoBehaviour, IInteracaoAcaoProvider
{
    private GeradorDesafioQuadratica gerador;
    private bool modoDesafio = false;

    public event Action OnAlternarModo;
    public event Action OnNovoDesafio;
    public event Action OnResetarPosicao;

    void Start()
    {
        // Encontra o gerador de desafios na cena
        gerador = FindAnyObjectByType<GeradorDesafioQuadratica>();
        
        // Conecta automaticamente com provedores de ação tangíveis (ex: botão ArUco)
        var acoesTangiveis = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var c in acoesTangiveis)
        {
            if (c is IInteracaoAcaoProvider provider && c != this)
            {
                provider.OnNovoDesafio += () => 
                {
                    if (modoDesafio && gerador != null) gerador.GerarNovoDesafio();
                };
            }
        }

        // Inicia no modo sandbox (sem bolinhas)
        SetModo(false);
    }

    public void AlternarModo()
    {
        SetModo(!modoDesafio);
    }

    public void SetModo(bool desafio)
    {
        modoDesafio = desafio;
        if (gerador != null)
        {
            gerador.AtivarModo(desafio);
        }
        OnAlternarModo?.Invoke();
    }

    // Usamos OnGUI desenhado proceduralmente para ser super leve 
    // e não precisar configurar Canvas manualmente no Editor do Unity
    void OnGUI()
    {
        // Escalar GUI para ficar legível em telas mobile (celular em pé ou deitado)
        // Baseamos a escala numa tela padrão de 1080p
        float escala = Screen.width > Screen.height ? Screen.height / 1080f : Screen.width / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(escala, escala, 1f));

        int larguraBotao = 260;
        int alturaBotao = 110;
        int margem = 20;

        GUI.skin.button.fontSize = 35;
        GUI.skin.box.fontSize = 35;

        // Fundo semitransparente no topo
        float telaEscaladaWidth = Screen.width / escala;
        GUI.Box(new Rect(margem, margem, telaEscaladaWidth - margem * 2, alturaBotao + margem * 2), "");

        // --- Botão Modo Livre (Sandbox) ---
        GUI.color = !modoDesafio ? Color.green : Color.white;
        if (GUI.Button(new Rect(margem * 2, margem * 2, larguraBotao, alturaBotao), "Livre (Sandbox)"))
        {
            SetModo(false);
        }

        // --- Botão Modo Desafio ---
        GUI.color = modoDesafio ? Color.green : Color.white;
        if (GUI.Button(new Rect(margem * 3 + larguraBotao, margem * 2, larguraBotao, alturaBotao), "Desafios"))
        {
            SetModo(true);
        }
        
        GUI.color = Color.white; // Reseta cor

        // --- Botão Novo Desafio (Só aparece se estiver no Modo Desafio) ---
        if (modoDesafio && gerador != null)
        {
            GUI.color = new Color(1f, 0.8f, 0f); // Amarelo
            float xNovoDesafio = telaEscaladaWidth - margem * 2 - larguraBotao;
            if (GUI.Button(new Rect(xNovoDesafio, margem * 2, larguraBotao, alturaBotao), "Novo Desafio"))
            {
                gerador.GerarNovoDesafio();
                OnNovoDesafio?.Invoke();
            }
            GUI.color = Color.white;
        }
    }
}
