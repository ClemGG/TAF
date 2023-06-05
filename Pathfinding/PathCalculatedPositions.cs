using Unity.Collections;

/// <summary>
/// Contient la liste des points du chemin calculé par le PathFinder
/// </summary>
public struct PathCalculatedPositions
{
    #region Variables d'instance

    /// <summary>
    /// Les positions de chaque point du chemin
    /// </summary>
    public NativeList<PathPositionBE> Positions;

    #endregion

    #region Constructeurs

    /// <summary>
    /// Constructeur par défaut
    /// </summary>
    /// <param name="positions">La liste des positions de ce chemin dans la scène</param>
    public PathCalculatedPositions(NativeList<PathPositionBE> positions = default) : this(positions.AsArray())
    {

    }

    /// <summary>
    /// Constructeur par défaut
    /// </summary>
    /// <param name="positions">La liste des positions de ce chemin dans la scène</param>
    public PathCalculatedPositions(NativeArray<PathPositionBE> positions = default)
    {
        this.Positions = new NativeList<PathPositionBE>(positions.Length, Allocator.Persistent);

        for (int i = 0; i < positions.Length; i++)
        {
            this.Positions.Add(positions[i]);
        }
    }

    #endregion
}
