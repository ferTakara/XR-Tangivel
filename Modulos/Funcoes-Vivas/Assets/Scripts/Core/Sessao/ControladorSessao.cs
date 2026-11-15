using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRTangivel.Core
{
    /// <summary>
    /// Dono único do estado da sessão: modo atual (livre ou desafio), pedidos de novo desafio
    /// e registro de tempo/eventos de cada desafio para o experimento da IC.
    ///
    /// As interfaces de cada plataforma (OnGUI no celular, painel 3D no Quest, gestos, cartão 45)
    /// só chamam métodos daqui. Se não houver um na cena, ControladorSessao.Obter() cria um.
    /// Coloque-o na cena para poder preencher a "condição" do experimento no Inspector.
    /// </summary>
    public class ControladorSessao : MonoBehaviour
    {
        [Header("Experimento")]
        [Tooltip("Rótulo da condição gravado no CSV. Ex.: C1 = celular + cartões, C2 = Quest + cartões, C3 = Quest + dials virtuais")]
        public string condicao = "";

        [Tooltip("Grava os eventos de cada desafio em CSV (Application.persistentDataPath/sessoes)")]
        public bool gravarCsv = true;

        [Header("Referências (opcional: procuradas na cena se vazias)")]
        public GeradorDesafioQuadratica gerador;
        public VisualizadorFuncaoQuadratica visualizador;

        public bool ModoDesafio { get; private set; }
        public bool TemGerador => gerador != null;

        /// <summary>Disparado sempre que o modo muda (true = desafio).</summary>
        public event Action<bool> OnModoAlterado;

        private static ControladorSessao instancia;

        private RegistroSessaoCsv registro;
        private readonly List<IInteracaoAcaoProvider> provedores = new List<IInteracaoAcaoProvider>();
        private bool inicializado;
        private bool desafioEmAndamento;
        private float inicioDesafio;
        private int numeroDesafio;

        /// <summary>Instância da cena (cria uma se não existir).</summary>
        public static ControladorSessao Obter()
        {
            if (instancia != null) return instancia;
            instancia = FindAnyObjectByType<ControladorSessao>();
            if (instancia == null)
                instancia = new GameObject("ControladorSessao").AddComponent<ControladorSessao>();
            return instancia;
        }

        void Awake()
        {
            if (instancia != null && instancia != this)
            {
                Debug.LogWarning("[Sessão] Já existe um ControladorSessao na cena; removendo o duplicado.");
                Destroy(this);
                return;
            }
            instancia = this;
        }

        void Start() => Inicializar();

        void OnDestroy()
        {
            if (instancia == this) instancia = null;
            if (!inicializado) return;

            if (desafioEmAndamento) Registrar("abandonado");
            if (gerador != null)
            {
                gerador.OnDesafioGerado -= AoGerarDesafio;
                gerador.OnDesafioResolvido -= AoResolverDesafio;
            }
            foreach (var p in provedores)
            {
                if (p == null) continue;
                p.OnNovoDesafio -= PedirNovoDesafio;
                p.OnAlternarModo -= AlternarModo;
            }
            provedores.Clear();
            registro?.Fechar();
        }

        // Pode rodar antes do Start, se alguém chamar um método público no próprio Start
        private void Inicializar()
        {
            if (inicializado) return;
            inicializado = true;

            if (gerador == null) gerador = FindAnyObjectByType<GeradorDesafioQuadratica>();
            if (visualizador == null) visualizador = FindAnyObjectByType<VisualizadorFuncaoQuadratica>();

            if (gerador != null)
            {
                gerador.OnDesafioGerado += AoGerarDesafio;
                gerador.OnDesafioResolvido += AoResolverDesafio;
            }

            // Botões e gestos tangíveis (ex.: BotaoTangivelAruco) só emitem eventos; quem decide é a sessão
            foreach (var c in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (c is IInteracaoAcaoProvider p)
                {
                    p.OnNovoDesafio += PedirNovoDesafio;
                    p.OnAlternarModo += AlternarModo;
                    provedores.Add(p);
                }
            }

            if (gravarCsv) registro = new RegistroSessaoCsv(condicao);
            Registrar("sessao_iniciada");

            AplicarModo(false); // começa no modo livre (sandbox)
        }

        // ================================================================
        // API pública (chamada pelas interfaces de cada plataforma)
        // ================================================================

        public void AlternarModo()
        {
            Inicializar();
            AplicarModo(!ModoDesafio);
        }

        public void SetModo(bool desafio)
        {
            Inicializar();
            if (desafio != ModoDesafio) AplicarModo(desafio);
        }

        /// <summary>Gera um novo desafio. Ignorado no modo livre.</summary>
        public void PedirNovoDesafio()
        {
            Inicializar();
            if (!ModoDesafio || gerador == null) return;
            gerador.GerarNovoDesafio(); // AoGerarDesafio cuida do registro
        }

        // ================================================================
        // Internos
        // ================================================================

        private void AplicarModo(bool desafio)
        {
            if (!desafio && desafioEmAndamento)
            {
                Registrar("abandonado");
                desafioEmAndamento = false;
            }

            ModoDesafio = desafio;
            if (gerador != null) gerador.AtivarModo(desafio); // pode gerar o primeiro desafio (AoGerarDesafio)

            // Entrou no modo desafio com um desafio já existente: o cronômetro começa agora
            if (desafio && !desafioEmAndamento && gerador != null) IniciarDesafioAtual();

            Registrar(desafio ? "modo_desafio" : "modo_livre");
            OnModoAlterado?.Invoke(desafio);
            Debug.Log($"[Sessão] Modo: {(desafio ? "Desafios" : "Livre (Sandbox)")}");
        }

        private void AoGerarDesafio()
        {
            if (!ModoDesafio) return; // desafios gerados no modo livre não contam
            if (desafioEmAndamento) Registrar("pulado");
            IniciarDesafioAtual();
        }

        private void IniciarDesafioAtual()
        {
            numeroDesafio++;
            inicioDesafio = Time.time;
            desafioEmAndamento = true;
            Registrar("iniciado");
        }

        private void AoResolverDesafio()
        {
            if (!ModoDesafio || !desafioEmAndamento) return;
            Registrar("resolvido");
            Debug.Log($"[Sessão] Desafio {numeroDesafio} resolvido em {Time.time - inicioDesafio:F1}s");
            desafioEmAndamento = false;
        }

        private void Registrar(string evento)
        {
            if (registro == null) return;
            Vector3 alvo = gerador != null ? gerador.Alvo : new Vector3(float.NaN, float.NaN, float.NaN);
            Vector3 atual = visualizador != null
                ? new Vector3(visualizador.coeficienteA, visualizador.coeficienteB, visualizador.coeficienteC)
                : new Vector3(float.NaN, float.NaN, float.NaN);
            float tempo = desafioEmAndamento ? Time.time - inicioDesafio : float.NaN;
            registro.Registrar(evento, numeroDesafio, alvo, atual, tempo);
        }
    }
}
