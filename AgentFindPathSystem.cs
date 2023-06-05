using BIMFlux.Shared.DTO.Values;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Charg� de tracer des itin�raires pour les agents
/// </summary>
[BurstCompile]
[UpdateBefore(typeof(SimulationSystem))]
public partial struct AgentFindPathSystem : ISystem
{
    #region Variables d'instance

    /// <summary>
    /// L'arch�type des entit�s contenant les infos des chemins
    /// </summary>
    private EntityArchetype _pathArchetype;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Quand le syst�me est cr��
    /// </summary>
    /// <param name="state">L'�tat du syst�me � l'instant t</param>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<RandomCD>();

        // Cr�e un arch�type pour garder en m�moire les infos de chaque chemin

        NativeArray<ComponentType> types = new(6, Allocator.Temp);
        types[0] = ComponentType.ReadWrite<PathEntityTagCD>();
        types[1] = ComponentType.ReadWrite<PathEntityFloorIndexCD>();
        types[2] = ComponentType.ReadWrite<PathEntityStartEndCD>();
        types[3] = ComponentType.ReadWrite<PathEntityPointDtoBE>();
        types[4] = ComponentType.ReadWrite<PathEntityPointPositionBE>();
        types[5] = ComponentType.ReadWrite<PathEntitySegmentTagBE>();

        this._pathArchetype = state.EntityManager.CreateArchetype(types);
    }

    /// <summary>
    /// Quand le syst�me est d�truit
    /// </summary>
    /// <param name="state">L'�tat du syst�me � l'instant t</param>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    /// <summary>
    /// Quand le syst�me est m�j
    /// </summary>
    /// <param name="state">L'�tat du syst�me � l'instant t</param>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefRW<RandomCD> randomRW = SystemAPI.GetSingletonRW<RandomCD>();
        EntityCommandBuffer.ParallelWriter ecbJobs = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        #region Recherche des destinations

        EntityQuery agentsQuery = SystemAPI.QueryBuilder().WithAll<AgentFindPathCD>().Build();
        EntityQuery ezSurfacesQuery = SystemAPI.QueryBuilder().WithAll<EZSurfaceTag>().Build();

        NativeArray<Entity> agentsEntities = agentsQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<Entity> ezSurfacesEntities = ezSurfacesQuery.ToEntityArray(Allocator.TempJob);

        NativeArray<bool> destinationsWereFoundResults = new(agentsEntities.Length, Allocator.TempJob);
        NativeArray<float3> destinationsResults = new(agentsEntities.Length, Allocator.TempJob);

        JobHandle destinationsHandle = new GetAgentsDestinationsJob
        {
            Random = randomRW,
            AgentsRO = agentsEntities,
            EZSurfacesRO = ezSurfacesEntities,

            AgentPropertiesLookup = SystemAPI.GetComponentLookup<AgentPropertiesCD>(true),
            AgentStateLookup = SystemAPI.GetComponentLookup<AgentStateCD>(true),
            SurfacesPosesLookup = SystemAPI.GetComponentLookup<SurfacePositionCD>(true),
            EZMetadatasLookup = SystemAPI.GetComponentLookup<EZSurfaceMetadatasCD>(true),
            ProfilesLookup = SystemAPI.GetBufferLookup<SurfaceProfilesBE>(true),
            DestinationsLookup = SystemAPI.GetBufferLookup<EZSurfaceDestinationsBE>(true),

            DestinationsWereFoundWO = destinationsWereFoundResults,
            DestinationsWO = destinationsResults,
        }
        .Schedule(agentsEntities.Length, 64, state.Dependency);

        #endregion

        #region R�cup�re les PathPoints les plus proches du d�part et de la destination de l'agent

        EntityQuery floorInfosQuery = SystemAPI.QueryBuilder().WithAll<FloorInfoTagCD>().Build();
        NativeArray<Entity> floorInfosEntities = floorInfosQuery.ToEntityArray(Allocator.TempJob);

        NativeArray<float3> startPosesResults = new(agentsEntities.Length, Allocator.TempJob);
        NativeArray<float3> endPosesResults = new(agentsEntities.Length, Allocator.TempJob);
        NativeArray<Entity> pathEntitiesInCacheResults = new(agentsEntities.Length, Allocator.TempJob);

        JobHandle posesHandle = new GetStartEndPositionsJob
        {
            AgentsRO = agentsEntities,
            FloorInfosRO = floorInfosEntities,
            DestinationsWereFoundRO = destinationsWereFoundResults,
            DestinationsRO = destinationsResults,

            AgentTransformsLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            AgentPropertiesLookup = SystemAPI.GetComponentLookup<AgentPropertiesCD>(true),
            AgentStateLookup = SystemAPI.GetComponentLookup<AgentStateCD>(true),
            SurfacesPosesLookup = SystemAPI.GetComponentLookup<SurfacePositionCD>(true),
            FloorNodesLookup = SystemAPI.GetBufferLookup<FloorInfoNodeBE>(true),

            StartPosesWO = startPosesResults,
            EndPosesWO = endPosesResults,
        }
        .Schedule(agentsEntities.Length, 64, destinationsHandle);

        state.Dependency = posesHandle;
        state.Dependency.Complete();
        ezSurfacesEntities.Dispose();

        #endregion

        #region Recherche de chemins

        // On n'a pas d'autre choix que de chercher les chemins dans le cache
        // et les calculer sur le thread ppal
        // pour s'assurer que le cache soit m�j entre chaque agent
        // et que chaque agent cherche un cache avec les entit�s cr��es � temps

        for (int agentIndex = 0; agentIndex < agentsEntities.Length; agentIndex++)
        {
            if (!destinationsWereFoundResults[agentIndex])
            {
                continue;
            }

            #region R�cup�ration des param�tres

            Entity agentE = agentsEntities[agentIndex];
            DynamicBuffer<AgentPathTagBE> agentTags = state.EntityManager.GetBuffer<AgentPathTagBE>(agentE, true);

            float3 startPos = startPosesResults[agentIndex];
            float3 endPos = endPosesResults[agentIndex];

            int floorIndex = state.EntityManager.GetComponentData<AgentStateCD>(agentE).FloorSurfaceIDs.x;
            Entity floorE = floorInfosEntities[floorIndex];
            DynamicBuffer<FloorInfoNodeBE> nodes = state.EntityManager.GetBuffer<FloorInfoNodeBE>(floorE, true);
            DynamicBuffer<FloorInfoArcBE> arcs = state.EntityManager.GetBuffer<FloorInfoArcBE>(floorE, true);
            DynamicBuffer<FloorInfoNodeNeighbourBE> nodesNeighbours = state.EntityManager.GetBuffer<FloorInfoNodeNeighbourBE>(floorE, true);
            DynamicBuffer<FloorInfoArcTagBE> arcsTags = state.EntityManager.GetBuffer<FloorInfoArcTagBE>(floorE, true);

            #endregion

            #region R�cup�re un chemin dans le cache

            Entity pathEntity = this.GetPathInCache(floorIndex, startPos, endPos, agentTags, ref state);
            pathEntitiesInCacheResults[agentIndex] = pathEntity;

            // Evite le calcul de chemin si l'agent en a trouv� un dans le cache

            if (pathEntity != Entity.Null)
            {
                continue;
            }

            #endregion

            #region Calcul de chemin (Dijkstra)

            NativeHashSet<FixedString64Bytes> registeredTags = new(0, Allocator.Temp);
            NativeList<PathPositionBE> positions = new(nodes.Length, Allocator.Temp);
            NativeList<FloorInfoNodeBE> unvisited = new(nodes.Length, Allocator.Temp);
            NativeHashMap<FloorInfoNodeBE, FloorInfoNodeBE> previous = new(nodes.Length, Allocator.Temp);
            NativeHashMap<FloorInfoNodeBE, float> distances = new(arcs.Length, Allocator.Temp);
            FloorInfoNodeComparer comparer = new(distances);

            // Initialise la liste de distances

            this.InitNodesDistancesList(startPos, nodes, unvisited, distances);

            // Tant qu'il reste des points � �valuer

            while (unvisited.Length > 0)
            {
                unvisited.Sort(comparer);

                FloorInfoNodeBE current = unvisited[0];
                unvisited.RemoveAt(0);

                // Retrace le chemin si on a atteint l'arriv�e

                if (current.Position.Equals(endPos))
                {
                    this.BakeFinalPath(current, previous, positions);
                    break;
                }

                // Pour chaque noeud voisin

                for (int i = 0; i < nodesNeighbours.Length; i++)
                {
                    FloorInfoNodeNeighbourBE neighbourInfo = nodesNeighbours[i];

                    if (neighbourInfo.IDs.x == current.DTOID)
                    {
                        FloorInfoNodeBE neighbour = nodes[neighbourInfo.IDs.y];

                        // R�cup�re l'arc entre ces deux noeuds

                        FloorInfoArcBE arc = this.GetNeighbouringArc(current.DTOID, neighbour.DTOID, arcs);

                        // Compare les tags de l'arc et de l'agent

                        if (!this.ArcIsValid(arc, arcsTags, agentTags, registeredTags))
                        {
                            continue;
                        }

                        // On enregistre l'arc s'il est plus court que le pr�c�dent

                        this.GetNeighbourDistance(current, neighbour, arc, distances, previous);
                    }
                }
            }

            #endregion

            // Cr�e une entit� pour repr�senter le nouveau chemin,
            // et cr�e une copie avec ses positions invers�es
            // pour permettre de prendre le chemin en sens inverse.
            // On cr�e la copie en premier car les positions sont d�j� invers�es.

            this.CreatePathEntity(floorIndex, endPos, startPos, positions, registeredTags, ref state);

            positions.ReverseList();
            pathEntitiesInCacheResults[agentIndex] = this.CreatePathEntity(floorIndex, startPos, endPos, positions, registeredTags, ref state);
        }

        floorInfosEntities.Dispose();
        startPosesResults.Dispose();
        endPosesResults.Dispose();
        destinationsResults.Dispose();

        #endregion

        #region Assigne les chemins aux agents

        JobHandle clearHandle = new ClearAgentsPaths().ScheduleParallel(state.Dependency);

        JobHandle setPathsHandle = new AssignAgentsPathsJob
        {
            Ecb = ecbJobs,
            AgentsRO = agentsEntities,
            PathsRO = pathEntitiesInCacheResults,
            DestinationsWereFoundRO = destinationsWereFoundResults,

            AgentsTargetsLookup = SystemAPI.GetComponentLookup<SurfacePositionCD>(true),
            AgentsTransformsLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            PathPointsDTOsLookup = SystemAPI.GetBufferLookup<PathEntityPointDtoBE>(true),
            PathPointsPosesLookup = SystemAPI.GetBufferLookup<PathEntityPointPositionBE>(true),
        }
        .Schedule(agentsEntities.Length, 64, clearHandle);

        #endregion

        #region Change le syst�me des agents

        EntityQuery poiSurfacesQuery = SystemAPI.QueryBuilder().WithAll<POISurfaceTag>().Build();
        NativeArray<Entity> poiSurfacesEntities = poiSurfacesQuery.ToEntityArray(Allocator.TempJob);

        JobHandle resolveHandle = new ResolveAgentsJob
        {
            Ecb = ecbJobs,
            AgentsRO = agentsEntities,
            POISurfacesRO = poiSurfacesEntities,
            DestinationsWereFoundRO = destinationsWereFoundResults,

            AgentsPropertiesLookup = SystemAPI.GetComponentLookup<AgentPropertiesCD>(true),
            AgentsStatesLookup = SystemAPI.GetComponentLookup<AgentStateCD>(true),
            AgentsTargetsLookup = SystemAPI.GetComponentLookup<SurfacePositionCD>(true),
            POIFreeSlotsLookup = SystemAPI.GetComponentLookup<POISurfaceFreeSlotsCD>(true),
            POICountersLookup = SystemAPI.GetComponentLookup<POISurfaceFreeSlotsCounterCD>(true),
        }
        .Schedule(agentsEntities.Length, 64, setPathsHandle);

        resolveHandle.Complete();

        agentsEntities.Dispose();
        destinationsWereFoundResults.Dispose();
        pathEntitiesInCacheResults.Dispose();
        poiSurfacesEntities.Dispose();


        #endregion
    }

    #endregion

    #region Fonctions priv�es

    /// <summary>
    /// R�cup�re l'entit� d'un chemin dans le cache s'il correspond aux tags de l'agent
    /// </summary>
    /// <param name="floorIndex">L'id de l'�tage �valu�</param>
    /// <param name="startPos">La position de d�part de l'agent</param>
    /// <param name="endPos">La position d'arriv�e de l'agent</param>
    /// <param name="agentTags">Les tags de l'agent en cours</param>
    /// <param name="state">L'�tat interne du syst�me</param>
    /// <returns>L'entit� du chemin correspondant aux tags de l'agent</returns>
    private Entity GetPathInCache(int floorIndex,
                                  float3 startPos,
                                  float3 endPos,
                                  DynamicBuffer<AgentPathTagBE> agentTags,
                                  ref SystemState state)
    {
        // R�cup�re les PathPoints les plus proches du d�part et de la destination de l'agent et
        // v�rifie si un chemin correspondant n'est pas d�j� dans le cache

        foreach ((PathEntityTagCD pathCDTag, Entity pathE) in SystemAPI.Query<PathEntityTagCD>().WithEntityAccess())
        {
            PathEntityFloorIndexCD pathFloorIndex = state.EntityManager.GetComponentData<PathEntityFloorIndexCD>(pathE);
            PathEntityStartEndCD pathPoints = state.EntityManager.GetComponentData<PathEntityStartEndCD>(pathE);

            if (pathFloorIndex.FloorID == floorIndex && pathPoints.Start.Equals(startPos) && pathPoints.End.Equals(endPos))
            {
                DynamicBuffer<PathEntitySegmentTagBE> pathTags = state.EntityManager.GetBuffer<PathEntitySegmentTagBE>(pathE);

                // Le chemin est valide s'il n'a aucun tag,
                // ou si tous les tags de l'agent correspondent � ceux du chemin

                int nbMatchingTags = 0;

                for (int i = 0; i < agentTags.Length; i++)
                {
                    AgentPathTagBE agentPathTag = agentTags[i];

                    for (int j = 0; j < pathTags.Length; j++)
                    {
                        if (agentPathTag.PathTag == pathTags[j].Tag)
                        {
                            nbMatchingTags++;
                        }
                    }
                }

                // Si l'agent comprend tous les tags du chemin, il peut l'emprunter
                // (Si le compte est � 0, l'agent n'a aucun tag et peut emprunter tous les chemins)

                if (nbMatchingTags == agentTags.Length)
                {
                    return (pathE);
                }
            }
        }

        return (Entity.Null);
    }

    /// <summary>
    /// Initialise la liste de nodes et de distances � parcourir
    /// pour le calcul de chemin
    /// </summary>
    /// <param name="startPos">La position de d�part</param>
    /// <param name="nodes">Les nodes de l'�tage</param>
    /// <param name="unvisited">Les nodes encore non-�valu�es</param>
    /// <param name="distances">Les sommes des distances entre les nodes</param>
    private void InitNodesDistancesList(float3 startPos,
                                        DynamicBuffer<FloorInfoNodeBE> nodes,
                                        NativeList<FloorInfoNodeBE> unvisited,
                                        NativeHashMap<FloorInfoNodeBE, float> distances)
    {
        for (int i = 0; i < nodes.Length; i++)
        {
            FloorInfoNodeBE node = nodes[i];
            unvisited.Add(node);

            if (node.Position.Equals(startPos))
            {
                distances.Add(node, 0f);
                continue;
            }

            distances.Add(node, float.MaxValue);
        }
    }

    /// <summary>
    /// R�cup�re les positions de tous les points calcul�s par l'algorithme
    /// </summary>
    /// <param name="current">Le point �valu�</param>
    /// <param name="previous">La liste des points menant � <paramref name="current"/></param>
    /// <param name="positions">Les positions de chaque point � retourner</param>
    private void BakeFinalPath(FloorInfoNodeBE current,
                               NativeHashMap<FloorInfoNodeBE,
                               FloorInfoNodeBE> previous,
                               NativeList<PathPositionBE> positions)
    {
        // On retrace tous les noeuds un � un

        while (previous.ContainsKey(current))
        {
            positions.AddNoResize(new PathPositionBE
            {
                DTOID = current.DTOID,
                Position = current.Position,
            });

            current = previous[current];
        }

        positions.AddNoResize(new PathPositionBE
        {
            DTOID = current.DTOID,
            Position = current.Position,
        });
    }

    /// <summary>
    /// R�cup�re les infos de l'arc entre deux noeuds
    /// </summary>
    /// <param name="currentDTOID">Le noeud �valu�</param>
    /// <param name="neighbourDTOID">Le voisin du noeud "current"</param>
    /// <param name="arcs">La liste de tous les segments de l'�tage</param>
    /// <returns>L'arc entre les deux noeuds renseign�s</returns>
    private FloorInfoArcBE GetNeighbouringArc(int currentDTOID,
                                              int neighbourDTOID,
                                              DynamicBuffer<FloorInfoArcBE> arcs)
    {
        FloorInfoArcBE arc = arcs[0];

        for (int j = 0; j < arcs.Length; j++)
        {
            FloorInfoArcBE temp = arcs[j];

            if (temp.NodesIDs.x == currentDTOID && temp.NodesIDs.y == neighbourDTOID ||
               temp.NodesIDs.x == neighbourDTOID && temp.NodesIDs.y == currentDTOID)
            {
                arc = temp;
                break;
            }
        }

        return (arc);
    }

    /// <summary>
    /// D�termine si l'agent est autoris� � emprunter ce segment
    /// </summary>
    /// <param name="arc">L'arc �valu�</param>
    /// <param name="arcsTags">Les tags de l'arc</param>
    /// <param name="agentTags">Les tags de l'agent</param>
    /// <param name="registeredTags">Les tags finaux de l'entit� du chemin cr��</param>
    /// <returns>TRUE si les tags de l'agent correspondent � ceux de l'arc</returns>
    private bool ArcIsValid(FloorInfoArcBE arc,
                            DynamicBuffer<FloorInfoArcTagBE> arcsTags,
                            DynamicBuffer<AgentPathTagBE> agentTags,
                            NativeHashSet<FixedString64Bytes> registeredTags)
    {
        // Si l'agent a des tags empruntables, on n'accepte que les segments sans tags ou portant au moins 1 de ces tags.
        // S'il n'en a aucun, l'agent peut emprunter tous les chemins.

        // Par d�faut, l'arc est valide.
        // Si l'agent n'a aucun tag, on pourra sauter la comparaison suivante.

        bool arcIsValid = true;

        for (int i = 0; i < agentTags.Length; i++)
        {
            // Si l'agent a un tag, on l'invalide pour le comparer avec les tags de l'arc

            arcIsValid = false;
            AgentPathTagBE agentPathTag = agentTags[i];

            for (int j = 0; j < arcsTags.Length; j++)
            {
                FloorInfoArcTagBE arcTag = arcsTags[j];

                if (arcTag.NodesIDs.Equals(arc.NodesIDs) &&
                    arcTag.Tag == agentPathTag.PathTag)
                {
                    // Pour ajouter les tags � l'entit� du chemin plus tard.
                    // On utilise un hashSet pour �viter les doublons.

                    registeredTags.Add(arcTag.Tag);
                    arcIsValid = true;
                    break;
                }
            }

            // Si au moins 1 tag correspond, on �vite de calculer pour tous les autres tags

            if (arcIsValid)
            {
                break;
            }
        }

        return (arcIsValid);
    }

    /// <summary>
    /// Enregistre la distance la plus court entre le noeud et ses voisins
    /// </summary>
    /// <param name="current">Le noeud �valu�</param>
    /// <param name="neighbour">Le voisin du noeud actuel</param>
    /// <param name="arc">Le segment liant les deux noeuds</param>
    /// <param name="distances">La liste des distances les plus courtes du chemin</param>
    /// <param name="previous">La liste des points menant � <paramref name="current"/></param>
    private void GetNeighbourDistance(FloorInfoNodeBE current,
                                      FloorInfoNodeBE neighbour,
                                      FloorInfoArcBE arc,
                                      NativeHashMap<FloorInfoNodeBE, float> distances,
                                      NativeHashMap<FloorInfoNodeBE, FloorInfoNodeBE> previous)
    {
        // La distance du point de d�part � ce noeud

        float alt = distances[current] + arc.LengthSq;

        // On enregistre le chemin s'il est plus court que le pr�c�dent

        if (alt < distances[neighbour])
        {
            distances[neighbour] = alt;
            previous[neighbour] = current;
        }
    }

    /// <summary>
    /// Cr�e une entit� repr�sentant le nouveau chemin calcul�
    /// </summary>
    /// <param name="floorIndex">L'id de l'�tage du chemin</param>
    /// <param name="startPos">Le point de d�part du chemin</param>
    /// <param name="endPos">Le point d'arriv�e du chemin</param>
    /// <param name="positions">Les positions de chaque point du chemin</param>
    /// <param name="registeredTags">Les tags du chemin</param>
    /// <param name="state">L'�tat interne du syst�me</param>
    /// <returns>Une entit� contenant chaque point du chemin calcul�</returns>
    private Entity CreatePathEntity(int floorIndex,
                                    float3 startPos,
                                    float3 endPos,
                                    NativeList<PathPositionBE> positions,
                                    NativeHashSet<FixedString64Bytes> registeredTags,
                                    ref SystemState state)
    {
        Entity pathEntity = state.EntityManager.CreateEntity(this._pathArchetype);
        state.EntityManager.SetName(pathEntity, $"Cached path on floor {floorIndex}");
        state.EntityManager.SetComponentData(pathEntity, new PathEntityFloorIndexCD { FloorID = floorIndex });
        state.EntityManager.SetComponentData(pathEntity, new PathEntityStartEndCD { Start = startPos, End = endPos });

        DynamicBuffer<PathEntityPointDtoBE> dtosRW = state.EntityManager.GetBuffer<PathEntityPointDtoBE>(pathEntity, false);
        DynamicBuffer<PathEntityPointPositionBE> posesRW = state.EntityManager.GetBuffer<PathEntityPointPositionBE>(pathEntity, false);

        for (int i = 0; i < positions.Length; i++)
        {
            dtosRW.Add(new PathEntityPointDtoBE { DTOID = positions[i].DTOID });
            posesRW.Add(new PathEntityPointPositionBE { Position = positions[i].Position });
        }

        // R�cup�re les tags de tous les arcs du chemin (sans duplicat)

        DynamicBuffer<PathEntitySegmentTagBE> tagsRW = state.EntityManager.GetBuffer<PathEntitySegmentTagBE>(pathEntity, false);
        NativeArray<FixedString64Bytes> tagsArr = registeredTags.ToNativeArray(Allocator.Temp);

        for (int i = 0; i < tagsArr.Length; i++)
        {
            tagsRW.Add(new PathEntitySegmentTagBE { Tag = tagsArr[i] });
        }

        return (pathEntity);
    }

    #endregion

    #region Jobs

    /// <summary>
    /// R�cup�re les positions des destinations de chaque agent en parall�le
    /// </summary>
    [BurstCompile]
    private partial struct GetAgentsDestinationsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Pour choisir un point d'acc�s au hasard
        /// </summary>
        [NativeDisableUnsafePtrRestriction]
        public RefRW<RandomCD> Random;

        /// <summary>
        /// Les agents � �valuer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les points d'acc�s du b�timent
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> EZSurfacesRO;

        /// <summary>
        /// Les propri�t�s des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentPropertiesCD> AgentPropertiesLookup;

        /// <summary>
        /// Les �tats internes des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentStateCD> AgentStateLookup;

        /// <summary>
        /// Les positions de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public ComponentLookup<SurfacePositionCD> SurfacesPosesLookup;

        /// <summary>
        /// Les m�tadonn�es de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public ComponentLookup<EZSurfaceMetadatasCD> EZMetadatasLookup;

        /// <summary>
        /// Les profils de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public BufferLookup<SurfaceProfilesBE> ProfilesLookup;

        /// <summary>
        /// Les destinations de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public BufferLookup<EZSurfaceDestinationsBE> DestinationsLookup;

        /// <summary>
        /// Indique si des destinations ont �t� trouv�es
        /// </summary>
        [WriteOnly]
        public NativeArray<bool> DestinationsWereFoundWO;

        /// <summary>
        /// Les positions des destinations pour chaque agent
        /// </summary>
        [WriteOnly]
        public NativeArray<float3> DestinationsWO;

        #endregion

        #region Fonctions publiques

        /// <summary>
        /// R�cup�re les positions des destinations de chaque agent en parall�le
        /// </summary>
        /// <param name="index">La position de l'agent dans la liste</param>
        [BurstCompile]
        public void Execute(int index)
        {
            Entity agentE = this.AgentsRO[index];
            AgentPropertiesCD agentProp = this.AgentPropertiesLookup[agentE];
            AgentStateCD agentState = this.AgentStateLookup[agentE];
            SurfacePositionCD agentTarget = this.SurfacesPosesLookup[agentE];

            // La destination du chemin de l'agent sera sa TargetPosition
            // si la zone est sur le m�me �tage que lui.

            if (agentTarget.FloorSurfaceIDs.x == agentState.FloorSurfaceIDs.x)
            {
                this.DestinationsWereFoundWO[index] = true;
                this.DestinationsWO[index] = agentTarget.Centroid;
                return;
            }

            NativeList<int> eligibleEZSurfaces = new(this.EZSurfacesRO.Length, Allocator.Temp);

            #region Recherche de points d'acc�s valides

            // Cr�e une liste de toutes les issues du b�timent empruntables pour cet agent

            for (int i = 0; i < this.EZSurfacesRO.Length; i++)
            {
                Entity e = this.EZSurfacesRO[i];
                SurfacePositionCD pos = this.SurfacesPosesLookup[e];
                EZSurfaceMetadatasCD meta = this.EZMetadatasLookup[e];
                DynamicBuffer<SurfaceProfilesBE> profiles = this.ProfilesLookup[e];
                DynamicBuffer<EZSurfaceDestinationsBE> destinations = this.DestinationsLookup[e];

                // Si la zone est active et sur le m�me �tage que l'agent

                if (meta.ZoneIsActive && pos.FloorSurfaceIDs.x == agentState.FloorSurfaceIDs.x)
                {
                    // La surface n'est �ligible que si elle est empruntable par ce profil.
                    // S'il n'y a aucun profil, la surface accepte tout le monde.

                    bool acceptsAgent = profiles.Length == 0;

                    for (int j = 0; j < profiles.Length; j++)
                    {
                        if (profiles[j].AssociatedProfileGUID.Equals(agentProp.GUID))
                        {
                            acceptsAgent = true;
                            break;
                        }
                    }

                    if (Hint.Likely(acceptsAgent))
                    {
                        // Si elle accepte l'agent, on regarde ensuite si elle m�ne vers l'�tage de la destination

                        for (int j = 0; j < destinations.Length; j++)
                        {
                            // On regarde pour chaque surface laquelle poss�de le GUID de destination
                            // et se trouve � l'�tage de la cible de l'agent

                            for (int k = 0; k < this.EZSurfacesRO.Length; k++)
                            {
                                Entity destE = this.EZSurfacesRO[k];
                                EZSurfaceMetadatasCD destMeta = this.EZMetadatasLookup[destE];
                                SurfacePositionCD destPos = this.SurfacesPosesLookup[destE];

                                if (destPos.FloorSurfaceIDs.x == agentTarget.FloorSurfaceIDs.x &&
                                    destinations[j].DestinationGUID.Equals(destMeta.GUID))
                                {
                                    eligibleEZSurfaces.Add(i);
                                }
                            }
                        }
                    }
                }
            }

            #endregion

            #region Assigne la destination

            // On prend une entr�e au hasard et on y place l'agent au d�but de la simulation

            if (Hint.Likely(eligibleEZSurfaces.Length > 0))
            {
                eligibleEZSurfaces.ShuffleList(this.Random);

                int id = eligibleEZSurfaces[0];
                Entity destE = this.EZSurfacesRO[id];
                SurfacePositionCD pos = this.SurfacesPosesLookup[destE];

                this.DestinationsWereFoundWO[index] = true;
                this.DestinationsWO[index] = pos.Centroid;
            }

            #endregion
        }

        #endregion
    }

    /// <summary>
    /// R�cup�re les points de d�part et d'arriv�e de chaque agent
    /// </summary>
    [BurstCompile]
    private partial struct GetStartEndPositionsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Les agents � �valuer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les infos de chaque �tage
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> FloorInfosRO;

        /// <summary>
        /// Indique si des destinations ont �t� trouv�es
        /// </summary>
        [ReadOnly]
        public NativeArray<bool> DestinationsWereFoundRO;

        /// <summary>
        /// Les positions des destinations pour chaque agent
        /// </summary>
        [ReadOnly]
        public NativeArray<float3> DestinationsRO;

        /// <summary>
        /// Les transforms des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<LocalTransform> AgentTransformsLookup;

        /// <summary>
        /// Les propri�t�s des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentPropertiesCD> AgentPropertiesLookup;

        /// <summary>
        /// Les �tats internes des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentStateCD> AgentStateLookup;

        /// <summary>
        /// Les positions de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public ComponentLookup<SurfacePositionCD> SurfacesPosesLookup;

        /// <summary>
        /// Les nodes de chaque �tage
        /// </summary>
        [ReadOnly]
        public BufferLookup<FloorInfoNodeBE> FloorNodesLookup;

        /// <summary>
        /// Les points de d�part pour chaque agent
        /// </summary>
        [WriteOnly]
        public NativeArray<float3> StartPosesWO;

        /// <summary>
        /// Les points d'arriv�e pour chaque agent
        /// </summary>
        [WriteOnly]
        public NativeArray<float3> EndPosesWO;

        #endregion

        #region Fonctions priv�es

        /// <summary>
        /// R�cup�re les points de d�part et d'arriv�e de chaque agent
        /// </summary>
        /// <param name="index">La position de l'agent dans la liste</param>
        [BurstCompile]
        public void Execute(int index)
        {
            if (!this.DestinationsWereFoundRO[index])
            {
                return;
            }

            Entity agentE = this.AgentsRO[index];
            float3 agentPos = this.AgentTransformsLookup[agentE].Position;
            float3 destination = this.DestinationsRO[index];

            int floorIndex = this.AgentStateLookup[agentE].FloorSurfaceIDs.x;
            Entity floorE = this.FloorInfosRO[floorIndex];
            DynamicBuffer<FloorInfoNodeBE> nodes = this.FloorNodesLookup[floorE];

            float3 startPos = float3.zero;
            float3 endPos = float3.zero;
            float closestStartDstSq = float.MaxValue;
            float closestEndDstSq = float.MaxValue;

            for (int i = 0; i < nodes.Length; i++)
            {
                float3 pointPos = nodes[i].Position;

                float startDstSq = math.distancesq(agentPos, pointPos);
                float endDstSq = math.distancesq(destination, pointPos);

                if (startDstSq < closestStartDstSq)
                {
                    closestStartDstSq = startDstSq;
                    startPos = pointPos;
                }

                if (endDstSq < closestEndDstSq)
                {
                    closestEndDstSq = endDstSq;
                    endPos = pointPos;
                }
            }

            this.StartPosesWO[index] = startPos;
            this.EndPosesWO[index] = endPos;
        }

        #endregion
    }

    /// <summary>
    /// Vide les pr�c�dents chemins des agents
    /// </summary>
    [BurstCompile]
    private partial struct ClearAgentsPaths : IJobEntity
    {
        #region Fonctions publiques

        /// <summary>
        /// Vide les pr�c�dents chemins des agents
        /// </summary>
        /// <param name="agent">L'agent � r�initialiser</param>
        [BurstCompile]
        public void Execute(AgentFindPathAspect agent)
        {
            agent.ClearPath();
        }

        #endregion
    }

    /// <summary>
    /// Assigne un nouveau chemin � chaque agent en parall�le
    /// </summary>
    [BurstCompile]
    private partial struct AssignAgentsPathsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Pour cr�er les entit�s des chemins en parall�le
        /// </summary>
        public EntityCommandBuffer.ParallelWriter Ecb;

        /// <summary>
        /// Les agents � �valuer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les chemins calcul�s
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> PathsRO;

        /// <summary>
        /// Indique si des destinations on pu �tre trouv�es
        /// </summary>
        [ReadOnly]
        public NativeArray<bool> DestinationsWereFoundRO;

        /// <summary>
        /// Les transforms de tous les agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<LocalTransform> AgentsTransformsLookup;

        /// <summary>
        /// Les cibles de tous les agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<SurfacePositionCD> AgentsTargetsLookup;

        /// <summary>
        /// Les ids des points de tous les chemins
        /// </summary>
        [ReadOnly]
        public BufferLookup<PathEntityPointDtoBE> PathPointsDTOsLookup;

        /// <summary>
        /// Les positions des points de tous les chemins
        /// </summary>
        [ReadOnly]
        public BufferLookup<PathEntityPointPositionBE> PathPointsPosesLookup;

        #endregion

        #region Fonctions publiques

        /// <summary>
        /// Assigne un nouveau chemin � chaque agent en parall�le
        /// </summary>
        /// <param name="index">La position de l'agent dans la liste</param>
        [BurstCompile]
        public void Execute(int index)
        {
            if (!this.DestinationsWereFoundRO[index])
            {
                return;
            }

            Entity agentE = this.AgentsRO[index];
            Entity pathE = this.PathsRO[index];
            float3 agentPos = this.AgentsTransformsLookup[agentE].Position;
            float3 targetPos = this.AgentsTargetsLookup[agentE].Centroid;
            DynamicBuffer<PathEntityPointDtoBE> pathIDs = this.PathPointsDTOsLookup[pathE];
            DynamicBuffer<PathEntityPointPositionBE> pathPoses = this.PathPointsPosesLookup[pathE];

            this.Ecb.AppendToBuffer(index, agentE, new PathPositionBE
            {
                DTOID = -1,
                Position = agentPos,
            });

            for (int i = 0; i < pathPoses.Length; i++)
            {
                this.Ecb.AppendToBuffer(index, agentE, new PathPositionBE { DTOID = pathIDs[i], Position = pathPoses[i] });
            }

            this.Ecb.AppendToBuffer(index, agentE, new PathPositionBE
            {
                DTOID = -1,
                Position = targetPos,
            });
        }

        #endregion
    }

    /// <summary>
    /// D�truit les agents sans destination
    /// et passe les autres � l'AgentMoveSystem
    /// </summary>
    [BurstCompile]
    private partial struct ResolveAgentsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Pour cr�er les entit�s des chemins en parall�le
        /// </summary>
        public EntityCommandBuffer.ParallelWriter Ecb;

        /// <summary>
        /// Les agents � �valuer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les zones de travail du b�timent
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> POISurfacesRO;

        /// <summary>
        /// Indique si des destinations on pu �tre trouv�es
        /// </summary>
        [ReadOnly]
        public NativeArray<bool> DestinationsWereFoundRO;

        /// <summary>
        /// Les propri�t�s de tous les agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentPropertiesCD> AgentsPropertiesLookup;

        /// <summary>
        /// Les propri�t�s de tous les agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentStateCD> AgentsStatesLookup;

        /// <summary>
        /// Les cibles de tous les agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<SurfacePositionCD> AgentsTargetsLookup;

        /// <summary>
        /// Les places libres de chaque zone de travail
        /// </summary>
        [ReadOnly]
        public ComponentLookup<POISurfaceFreeSlotsCD> POIFreeSlotsLookup;

        /// <summary>
        /// Les compteurs des places libres de chaque zone de travail
        /// </summary>
        [ReadOnly]
        public ComponentLookup<POISurfaceFreeSlotsCounterCD> POICountersLookup;

        #endregion

        #region Fonctions publiques

        /// <summary>
        /// D�truit les agents sans destination
        /// </summary>
        /// <param name="index">La position de l'agent dans la liste</param>
        [BurstCompile]
        public void Execute(int index)
        {
            Entity agentE = this.AgentsRO[index];

            // On se d�barrasse de l'agent s'il n'a pas de destination,
            // et on lib�re la place qui lui a �t� assign�e

            if (Hint.Unlikely(!this.DestinationsWereFoundRO[index]))
            {
                AgentPropertiesCD agentProperties = this.AgentsPropertiesLookup[agentE];
                AgentStateCD agentState = this.AgentsStatesLookup[agentE];
                SurfacePositionCD agentTarget = this.AgentsTargetsLookup[agentE];

                // On r�cup�re l'entit� de sa destination

                for (int i = 0; i < this.POISurfacesRO.Length; i++)
                {
                    Entity poiE = this.POISurfacesRO[i];
                    SurfacePositionCD tempPos = this.AgentsTargetsLookup[poiE];

                    if (tempPos.FloorSurfaceIDs.Equals(agentTarget.FloorSurfaceIDs))
                    {
                        POISurfaceFreeSlotsCD slots = this.POIFreeSlotsLookup[poiE];
                        POISurfaceFreeSlotsCounterCD counter = this.POICountersLookup[poiE];

                        this.FreePOI(ref slots,
                                     ref counter,
                                     agentState.CurOccupationType,
                                     agentProperties.Type,
                                     agentProperties.Gender);

                        // R�assigne les components � l'entit� pour sauvegarder les changements

                        this.Ecb.SetComponent(index, poiE, slots);
                        this.Ecb.SetComponent(index, poiE, counter);

                        break;
                    }
                }

                this.EnableAgentDestroyState(index, agentE, this.Ecb);

#if UNITY_EDITOR
                UnityEngine.Debug.Log($"Erreur : Le b�timent n'a aucune ezSurface permettant � l'agent \"{agentProperties.Name}\" d'atteindre sa destination � l'�tage {agentTarget.FloorSurfaceIDs}. Veuillez modifier le fichier dat.");
#endif

                return;
            }

            // Si tout va bien, on lance son processus de d�placement

            this.EnableAgentMoveState(index, agentE, this.Ecb);
        }

        #endregion

        #region Fonctions priv�es

        /// <summary>
        /// Lib�re ou assigne une place de la zone de travail
        /// </summary>
        /// <param name="slots">Les salles libres de chaque zone</param>
        /// <param name="counter">Les compteurs de salles libres de chaque zone</param>
        /// <param name="occupationType">L'occupation concern�e</param>
        /// <param name="agentType">Le type de l'agent (nomade ou s�dentaire)</param>
        /// <param name="agentGender">TRUE : F ; FALSE : H</param>
        private void FreePOI(ref POISurfaceFreeSlotsCD slots,
                             ref POISurfaceFreeSlotsCounterCD counter,
                             OccupationType occupationType,
                             ProfileType agentType,
                             bool agentGender)
        {
            switch (occupationType)
            {
                case OccupationType.R�union:
                    slots.ReunionSlots++;
                    break;

                case OccupationType.Isolation:
                    slots.IsolationSlots++;
                    break;

                case OccupationType.Annexe:
                    slots.AnnexeSlots++;
                    break;

                case OccupationType.Pause:
                    slots.PauseSlots++;
                    break;

                case OccupationType.Technique:
                    slots.TechniqueSlots++;
                    break;

                case OccupationType.Travail:
                    if (agentType == ProfileType.Nomade)
                    {
                        slots.FlexOfficesSlots++;
                    }
                    else
                    {
                        slots.FixedOfficesSlots++;
                    }
                    break;

                case OccupationType.WC:
                    if (agentGender == true)
                    {
                        slots.FacilitiesFSlots++;
                    }
                    else
                    {
                        slots.FacilitiesMSlots++;
                    }
                    break;
            }

            counter.CurFreeSlotsCount++;
        }

        /// <summary>
        /// Arr�te la recherche de chemins
        /// et d�sactive l'agent
        /// </summary>
        /// <param name="sortKey">L'id de l'agent</param>
        /// <param name="agentE">L'entit� � d�sactiver</param>
        /// <param name="ecb">Permet de synchroniser les changements</param>
        private void EnableAgentDestroyState(int sortKey, Entity agentE, EntityCommandBuffer.ParallelWriter ecb)
        {
            ecb.SetComponentEnabled<AgentFindPathCD>(sortKey, agentE, false);
            ecb.SetComponentEnabled<AgentDestroyCD>(sortKey, agentE, true);
        }

        /// <summary>
        /// Arr�te la recherche de chemins
        /// et lance le processus de d�placement de l'agent
        /// </summary>
        /// <param name="sortKey">L'id de l'agent</param>
        /// <param name="agentE">L'entit� � d�sactiver</param>
        /// <param name="ecb">Permet de synchroniser les changements</param>
        private void EnableAgentMoveState(int sortKey, Entity agentE, EntityCommandBuffer.ParallelWriter ecb)
        {
            ecb.SetComponentEnabled<AgentFindPathCD>(sortKey, agentE, false);
            ecb.SetComponentEnabled<AgentMoveCD>(sortKey, agentE, true);
        }

        #endregion
    }

    #endregion
}