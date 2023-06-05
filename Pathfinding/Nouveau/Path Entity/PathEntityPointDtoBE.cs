using Unity.Entities;

/// <summary>
/// Représente les IDs des points d'un chemin
/// </summary>
public struct PathEntityPointDtoBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// L'ID du point représentant cette position
    /// </summary>
    public int DTOID;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Convertit le component en valeur
    /// </summary>
    /// <param name="comp">Le component</param>
    public static implicit operator int(PathEntityPointDtoBE comp)
    {
        return comp.DTOID;
    }

    /// <summary>
    /// Convertit la valeur en component
    /// </summary>
    /// <param name="value">La valeur</param>
    public static implicit operator PathEntityPointDtoBE(int value)
    {
        return new PathEntityPointDtoBE { DTOID = value };
    }

    #endregion
}
