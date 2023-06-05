using Unity.Entities;

/// <summary>
/// L'ID de l'�tage correspondant � l'entit� d'un chemin
/// calcul� dans le PathFinderSystem
/// </summary>
public struct PathEntityFloorIndexCD : IComponentData
{
    #region Variables d'instance

    /// <summary>
    /// L'ID de l'�tage de ce chemin
    /// </summary>
    public int FloorID;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Convertit le component en valeur
    /// </summary>
    /// <param name="comp">Le component</param>
    public static implicit operator int(PathEntityFloorIndexCD comp)
    {
        return comp.FloorID;
    }

    /// <summary>
    /// Convertit la valeur en component
    /// </summary>
    /// <param name="value">La valeur</param>
    public static implicit operator PathEntityFloorIndexCD(int value)
    {
        return new PathEntityFloorIndexCD { FloorID = value };
    }

    #endregion
}
