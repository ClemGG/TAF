using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Garde en mémoire les points et segments de chaque étage,
/// convertis pour être utilisable par le PathFinderSystem
/// </summary>
public struct PathFinder
{
    #region Propriétés

    /// <summary>
    /// Les points de l'étage actif
    /// </summary>
    public NativeList<PathNode> PointsList
    {
        get => this._pointsList;
    }

    /// <summary>
    /// Les IDs de tous les voisins de chaque point
    /// (x : ID du pathPoint ; y : ID du voisin)
    /// </summary>
    public NativeMultiHashMap<int, int> PointsNeighborsIDsList
    {
        get => this._pointsNeighborsIDsList;
    }

    /// <summary>
    /// Les segments de l'étage actif
    /// </summary>
    public NativeList<PathArc> SegmentsList
    {
        get => this._segmentsList;
    }

    /// <summary>
    /// Les tags de chaque segment de l'étage
    /// (x : IDs du pathSegment ; y : le tag)
    /// </summary>
    public NativeMultiHashMap<int2, PathSegmentTagsBE> ArcsTagsList
    {
        get => this._arcsTagsList;
    }

    #endregion

    #region Variables d'instance

    /// <summary>
    /// Les points de l'étage actif
    /// </summary>
    private NativeList<PathNode> _pointsList;

    /// <summary>
    /// Les IDs de tous les voisins de chaque point
    /// (x : ID du pathPoint ; y : ID du voisin)
    /// </summary>
    private NativeMultiHashMap<int, int> _pointsNeighborsIDsList;

    /// <summary>
    /// Les segments de l'étage actif
    /// </summary>
    private NativeList<PathArc> _segmentsList;

    /// <summary>
    /// Les tags de chaque segment de l'étage
    /// (x : IDs du pathSegment ; y : le tag)
    /// </summary>
    private NativeMultiHashMap<int2, PathSegmentTagsBE> _arcsTagsList;

    #endregion

    #region Constructeurs

    /// <summary>
    /// Construit un PathFinder avec les points et segments de l'étage actuel
    /// </summary>
    /// <param name="pointsList">Les points de l'étage actif</param>
    /// <param name="segmentsList">Les segments de l'étage actif</param>
    public PathFinder(NativeList<PathPointCD> pointsList,
                               NativeList<PathSegmentAspect> segmentsList)
    {
        this._pointsList = new NativeList<PathNode>(pointsList.Length, Allocator.Persistent);
        this._segmentsList = new NativeList<PathArc>(segmentsList.Length, Allocator.Persistent);
        this._pointsNeighborsIDsList = new NativeMultiHashMap<int, int>(1, Allocator.Persistent);
        this._arcsTagsList = new NativeMultiHashMap<int2, PathSegmentTagsBE>(1, Allocator.Persistent);

        for (int i = 0; i < pointsList.Length; i++)
        {
            PathPointCD point = pointsList[i];

            this._pointsList.Add(new PathNode
            {
                ID = i,
                DTOID = point.ID,
                Position = point.Position,
            });
        }

        // Assigne les ids des voisins une fois que tous les points
        // on leurs ids correctement assignés

        for (int i = 0; i < this._pointsList.Length; i++)
        {
            PathNode node = this._pointsList[i];
            NativeArray<int> neighborsIDs = this.GetNeighborsIDsOf(in node, this._pointsList, segmentsList);

            for (int j = 0; j < neighborsIDs.Length; j++)
            {
                this._pointsNeighborsIDsList.Add(node.ID, neighborsIDs[j]);
            }
        }

        // Assigne les ids des points de chaque segment
        // ainsi que les tags

        for (int i = 0; i < segmentsList.Length; i++)
        {
            PathSegmentAspect segment = segmentsList[i];
            int2 pointdIDs = this.GetPointsIDsOf(this._pointsList, in segment);

            this._segmentsList.Add(new PathArc
            {
                PointsIDs = pointdIDs,
                Cost = segment.Properties.ValueRO.LengthSq,
            });

            for (int j = 0; j < segment.Tags.Length; j++)
            {
                this._arcsTagsList.Add(pointdIDs, segment.Tags[j]);
            }
        }
    }

    #endregion

    #region Fonctions privées

    /// <summary>
    /// Obtient les IDs des noeuds adjacents à celui-ci
    /// </summary>
    /// <param name="node">Le point à évaluer</param>
    /// <param name="segmentsList">La liste des segments de l'étage</param>
    /// <returns></returns>
    private NativeArray<int> GetNeighborsIDsOf(in PathNode node, NativeList<PathNode> pointsList, NativeList<PathSegmentAspect> segmentsList)
    {
        NativeList<int> neighborsIDs = new(Allocator.Persistent);

        for (int i = 0; i < segmentsList.Length; i++)
        {
            PathSegmentCD segment = segmentsList[i].Properties.ValueRO;
            int dtoID = -1;

            if (segment.PointsIDs.x == node.DTOID)
            {
                dtoID = segment.PointsIDs.y;
            }
            else if (segment.PointsIDs.y == node.DTOID)
            {
                dtoID = segment.PointsIDs.x;
            }

            for (int j = 0; j < pointsList.Length; j++)
            {
                PathNode n = pointsList[j];
                if (n.DTOID == dtoID)
                {
                    neighborsIDs.Add(n.ID);
                }
            }
        }

        return (neighborsIDs.AsArray());
    }

    /// <summary>
    /// Assigne à chaque PathArc les IDs des PathNodes correspondant à ses points
    /// </summary>
    /// <param name="pointsList">La liste de tous les points</param>
    /// <param name="segment">Le segment à copier</param>
    /// <returns>Les IDs des deux PathNodes correspondant aux IDs du segment d'origine</returns>
    private int2 GetPointsIDsOf(NativeList<PathNode> pointsList, in PathSegmentAspect segment)
    {
        int2 pointsIDs = 0;

        for (int i = 0; i < pointsList.Length; i++)
        {
            PathNode n = pointsList[i];

            if (n.DTOID == segment.Properties.ValueRO.PointsIDs.x)
            {
                pointsIDs.x = n.ID;
            }
            if (n.DTOID == segment.Properties.ValueRO.PointsIDs.y)
            {
                pointsIDs.y = n.ID;
            }
        }

        return (pointsIDs);
    }

    #endregion
}
