using System.Collections.Generic;
using Unity.Collections;

/// <summary>
/// Pour comparer 2 noeuds
/// </summary>
public struct FloorInfoNodeComparer : IComparer<FloorInfoNodeBE>
{
    #region Variables d'instance

    /// <summary>
    /// Les distances de chaque noeud au point de départ
    /// </summary>
    private NativeHashMap<FloorInfoNodeBE, float> _distances;

    #endregion

    #region Constructeur

    /// <summary>
    /// Le constructeur par défaut
    /// </summary>
    /// <param name="distances">Les distances entre noeuds à comparer</param>
    public FloorInfoNodeComparer(NativeHashMap<FloorInfoNodeBE, float> distances)
    {
        this._distances = distances;
    }

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Pour comparer 2 noeuds
    /// </summary>
    public int Compare(FloorInfoNodeBE x, FloorInfoNodeBE y)
    {
        if (this._distances.ContainsKey(y))
        {
            return this._distances[x].CompareTo(this._distances[y]);
        }

        return 0;
    }

    #endregion
}