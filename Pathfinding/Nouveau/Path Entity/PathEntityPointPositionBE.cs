using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Repr�sente un point du chemin qu'emprunteront
/// les agents pour rejoindre leurs zones de travail
/// </summary>
public struct PathEntityPointPositionBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// La position du point dans la sc�ne
    /// </summary>
    public float3 Position;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Convertit le component en valeur
    /// </summary>
    /// <param name="comp">Le component</param>
    public static implicit operator float3(PathEntityPointPositionBE comp)
    {
        return comp.Position;
    }

    /// <summary>
    /// Convertit la valeur en component
    /// </summary>
    /// <param name="value">La valeur</param>
    public static implicit operator PathEntityPointPositionBE(float3 value)
    {
        return new PathEntityPointPositionBE { Position = value };
    }

    #endregion
}
