namespace XRTangivel.Core
{
    /// <summary>
    /// Interface para fornecer coeficientes matemáticos (A, B, C) ao visualizador,
    /// independentemente se vêm de giroscópio de papel ArUco, Dials do Quest,
    /// sliders na tela ou gestos de pinça.
    /// </summary>
    public interface IParametroProvider
    {
        float CoeficienteA { get; }
        float CoeficienteB { get; }
        float CoeficienteC { get; }
        bool EstaAtivo { get; }
    }
}
