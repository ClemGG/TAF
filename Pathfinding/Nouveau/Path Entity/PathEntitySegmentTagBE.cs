using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Représente les tags de chaque segment du chemin
/// </summary>
public struct PathEntitySegmentTagBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// Le tag du segment actuel
    /// </summary>
    public FixedString64Bytes Tag;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Convertit le component en valeur
    /// </summary>
    /// <param name="comp">Le component</param>
    public static implicit operator FixedString64Bytes(PathEntitySegmentTagBE comp)
    {
        return comp.Tag;
    }

    /// <summary>
    /// Convertit la valeur en component
    /// </summary>
    /// <param name="value">La valeur</param>
    public static implicit operator PathEntitySegmentTagBE(FixedString64Bytes value)
    {
        return new PathEntitySegmentTagBE { Tag = value };
    }

    #endregion
}
