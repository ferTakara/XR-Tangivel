using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace XRTangivel.Core
{
    /// <summary>
    /// Grava os eventos da sessão em CSV (uma linha por evento) em
    /// Application.persistentDataPath/sessoes/sessao_AAAAMMDD_HHMMSS.csv.
    /// No Android: adb pull /sdcard/Android/data/&lt;pacote&gt;/files/sessoes
    /// </summary>
    public class RegistroSessaoCsv
    {
        public string Caminho { get; }

        private readonly string idSessao;
        private readonly string condicao;
        private readonly string plataforma;
        private StreamWriter escritor;

        public RegistroSessaoCsv(string condicao)
        {
            idSessao = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            this.condicao = Limpar(condicao);
            plataforma = Application.platform.ToString();

            try
            {
                string pasta = Path.Combine(Application.persistentDataPath, "sessoes");
                Directory.CreateDirectory(pasta);
                Caminho = Path.Combine(pasta, $"sessao_{idSessao}.csv");
                escritor = new StreamWriter(Caminho, false, new UTF8Encoding(false)) { AutoFlush = true };
                escritor.WriteLine("data_hora,sessao,plataforma,condicao,evento,desafio,alvo_a,alvo_b,alvo_c,a,b,c,tempo_s");
                Debug.Log($"[Sessão] Registrando eventos em: {Caminho}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Sessão] Não foi possível criar o CSV: {e.Message}");
                escritor = null;
            }
        }

        /// <param name="tempoSegundos">float.NaN deixa a coluna vazia.</param>
        public void Registrar(string evento, int desafio, Vector3 alvo, Vector3 atual, float tempoSegundos)
        {
            if (escritor == null) return;

            string linha = string.Join(",",
                DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                idSessao, plataforma, condicao, Limpar(evento),
                desafio.ToString(CultureInfo.InvariantCulture),
                Num(alvo.x), Num(alvo.y), Num(alvo.z),
                Num(atual.x), Num(atual.y), Num(atual.z),
                Num(tempoSegundos));

            try { escritor.WriteLine(linha); }
            catch (Exception e) { Debug.LogError($"[Sessão] Erro ao gravar CSV: {e.Message}"); }
        }

        public void Fechar()
        {
            escritor?.Dispose();
            escritor = null;
        }

        private static string Num(float v) =>
            float.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Limpar(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace(",", " ").Replace("\n", " ").Replace("\r", " ");
    }
}
