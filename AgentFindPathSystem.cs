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
/// Chargé de tracer des itinéraires pour les agents
/// </summary>
[BurstCompile]
[UpdateBefore(typeof(SimulationSystem))]
public partial struct AgentFindPathSystem : ISystem
{
    #region Variables d'instance

    /// <summary>
    /// L'archétype des entités contenant les infos des chemins
    /// </summary>
    private EntityArchetype _pathArchetype;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Quand le système est créé
    /// </summary>
    /// <param name="state">L'état du système à l'instant t</param>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<RandomCD>();

        // Crée un archétype pour garder en mémoire les infos de chaque chemin

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
    /// Quand le système est détruit
    /// </summary>
    /// <param name="state">L'état du système à l'instant t</param>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    /// <summary>
    /// Quand le système est màj
    /// </summary>
    /// <param name="state">L'état du système à l'instant t</param>
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

        #region Récupère les PathPoints les plus proches du départ et de la destination de l'agent

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
        // pour s'assurer que le cache soit màj entre chaque agent
        // et que chaque agent cherche un cache avec les entités créées à temps

        for (int agentIndex = 0; agentIndex < agentsEntities.Length; agentIndex++)
        {
            if (!destinationsWereFoundResults[agentIndex])
            {
                continue;
            }

            #region Récupération des paramètres

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

            #region Récupère un chemin dans le cache

            Entity pathEntity = this.GetPathInCache(floorIndex, startPos, endPos, agentTags, ref state);
            pathEntitiesInCacheResults[agentIndex] = pathEntity;

            // Evite le calcul de chemin si l'agent en a trouvé un dans le cache

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

            // Tant qu'il reste des points à évaluer

            while (unvisited.Length > 0)
            {
                unvisited.Sort(comparer);

                FloorInfoNodeBE current = unvisited[0];
                unvisited.RemoveAt(0);

                // Retrace le chemin si on a atteint l'arrivée

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

                        // Récupère l'arc entre ces deux noeuds

                        FloorInfoArcBE arc = this.GetNeighbouringArc(current.DTOID, neighbour.DTOID, arcs);

                        // Compare les tags de l'arc et de l'agent

                        if (!this.ArcIsValid(arc, arcsTags, agentTags, registeredTags))
                        {
                            continue;
                        }

                        // On enregistre l'arc s'il est plus court que le précédent

                        this.GetNeighbourDistance(current, neighbour, arc, distances, previous);
                    }
                }
            }

            #endregion

            // Crée une entité pour représenter le nouveau chemin,
            // et crée une copie avec ses positions inversées
            // pour permettre de prendre le chemin en sens inverse.
            // On crée la copie en premier car les positions sont déjà inversées.

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

        #region Change le système des agents

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

    #region Fonctions privées

    /// <summary>
    /// Récupère l'entité d'un chemin dans le cache s'il correspond aux tags de l'agent
    /// </summary>
    /// <param name="floorIndex">L'id de l'étage évalué</param>
    /// <param name="startPos">La position de départ de l'agent</param>
    /// <param name="endPos">La position d'arrivée de l'agent</param>
    /// <param name="agentTags">Les tags de l'agent en cours</param>
    /// <param name="state">L'état interne du système</param>
    /// <returns>L'entité du chemin correspondant aux tags de l'agent</returns>
    private Entity GetPathInCache(int floorIndex,
                                  float3 startPos,
                                  float3 endPos,
                                  DynamicBuffer<AgentPathTagBE> agentTags,
                                  ref SystemState state)
    {
        // Récupère les PathPoints les plus proches du départ et de la destination de l'agent et
        // vérifie si un chemin correspondant n'est pas déjà dans le cache

        foreach ((PathEntityTagCD pathCDTag, Entity pathE) in SystemAPI.Query<PathEntityTagCD>().WithEntityAccess())
        {
            PathEntityFloorIndexCD pathFloorIndex = state.EntityManager.GetComponentData<PathEntityFloorIndexCD>(pathE);
            PathEntityStartEndCD pathPoints = state.EntityManager.GetComponentData<PathEntityStartEndCD>(pathE);

            if (pathFloorIndex.FloorID == floorIndex && pathPoints.Start.Equals(startPos) && pathPoints.End.Equals(endPos))
            {
                DynamicBuffer<PathEntitySegmentTagBE> pathTags = state.EntityManager.GetBuffer<PathEntitySegmentTagBE>(pathE);

                // Le chemin est valide s'il n'a aucun tag,
                // ou si tous les tags de l'agent correspondent à ceux du chemin

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
                // (Si le compte est à 0, l'agent n'a aucun tag et peut emprunter tous les chemins)

                if (nbMatchingTags == agentTags.Length)
                {
                    return (pathE);
                }
            }
        }

        return (Entity.Null);
    }

    /// <summary>
    /// Initialise la liste de nodes et de distances à parcourir
    /// pour le calcul de chemin
    /// </summary>
    /// <param name="startPos">La position de départ</param>
    /// <param name="nodes">Les nodes de l'étage</param>
    /// <param name="unvisited">Les nodes encore non-évaluées</param>
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
    /// Récupère les positions de tous les points calculés par l'algorithme
    /// </summary>
    /// <param name="current">Le point évalué</param>
    /// <param name="previous">La liste des points menant à <paramref name="current"/></param>
    /// <param name="positions">Les positions de chaque point à retourner</param>
    private void BakeFinalPath(FloorInfoNodeBE current,
                               NativeHashMap<FloorInfoNodeBE,
                               FloorInfoNodeBE> previous,
                               NativeList<PathPositionBE> positions)
    {
        // On retrace tous les noeuds un à un

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
    /// Récupère les infos de l'arc entre deux noeuds
    /// </summary>
    /// <param name="currentDTOID">Le noeud évalué</param>
    /// <param name="neighbourDTOID">Le voisin du noeud "current"</param>
    /// <param name="arcs">La liste de tous les segments de l'étage</param>
    /// <returns>L'arc entre les deux noeuds renseignés</returns>
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
    /// Détermine si l'agent est autorisé à emprunter ce segment
    /// </summary>
    /// <param name="arc">L'arc évalué</param>
    /// <param name="arcsTags">Les tags de l'arc</param>
    /// <param name="agentTags">Les tags de l'agent</param>
    /// <param name="registeredTags">Les tags finaux de l'entité du chemin créé</param>
    /// <returns>TRUE si les tags de l'agent correspondent à ceux de l'arc</returns>
    private bool ArcIsValid(FloorInfoArcBE arc,
                            DynamicBuffer<FloorInfoArcTagBE> arcsTags,
                            DynamicBuffer<AgentPathTagBE> agentTags,
                            NativeHashSet<FixedString64Bytes> registeredTags)
    {
        // Si l'agent a des tags empruntables, on n'accepte que les segments sans tags ou portant au moins 1 de ces tags.
        // S'il n'en a aucun, l'agent peut emprunter tous les chemins.

        // Par défaut, l'arc est valide.
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
                    // Pour ajouter les tags à l'entité du chemin plus tard.
                    // On utilise un hashSet pour éviter les doublons.

                    registeredTags.Add(arcTag.Tag);
                    arcIsValid = true;
                    break;
                }
            }

            // Si au moins 1 tag correspond, on évite de calculer pour tous les autres tags

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
    /// <param name="current">Le noeud évalué</param>
    /// <param name="neighbour">Le voisin du noeud actuel</param>
    /// <param name="arc">Le segment liant les deux noeuds</param>
    /// <param name="distances">La liste des distances les plus courtes du chemin</param>
    /// <param name="previous">La liste des points menant à <paramref name="current"/></param>
    private void GetNeighbourDistance(FloorInfoNodeBE current,
                                      FloorInfoNodeBE neighbour,
                                      FloorInfoArcBE arc,
                                      NativeHashMap<FloorInfoNodeBE, float> distances,
                                      NativeHashMap<FloorInfoNodeBE, FloorInfoNodeBE> previous)
    {
        // La distance du point de départ à ce noeud

        float alt = distances[current] + arc.LengthSq;

        // On enregistre le chemin s'il est plus court que le précédent

        if (alt < distances[neighbour])
        {
            distances[neighbour] = alt;
            previous[neighbour] = current;
        }
    }

    /// <summary>
    /// Crée une entité représentant le nouveau chemin calculé
    /// </summary>
    /// <param name="floorIndex">L'id de l'étage du chemin</param>
    /// <param name="startPos">Le point de départ du chemin</param>
    /// <param name="endPos">Le point d'arrivée du chemin</param>
    /// <param name="positions">Les positions de chaque point du chemin</param>
    /// <param name="registeredTags">Les tags du chemin</param>
    /// <param name="state">L'état interne du système</param>
    /// <returns>Une entité contenant chaque point du chemin calculé</returns>
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

        // Récupère les tags de tous les arcs du chemin (sans duplicat)

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
    /// Récupère les positions des destinations de chaque agent en parallèle
    /// </summary>
    [BurstCompile]
    private partial struct GetAgentsDestinationsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Pour choisir un point d'accès au hasard
        /// </summary>
        [NativeDisableUnsafePtrRestriction]
        public RefRW<RandomCD> Random;

        /// <summary>
        /// Les agents à évaluer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les points d'accès du bâtiment
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> EZSurfacesRO;

        /// <summary>
        /// Les propriétés des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentPropertiesCD> AgentPropertiesLookup;

        /// <summary>
        /// Les états internes des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentStateCD> AgentStateLookup;

        /// <summary>
        /// Les positions de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public ComponentLookup<SurfacePositionCD> SurfacesPosesLookup;

        /// <summary>
        /// Les métadonnées de toutes les surfaces
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
        /// Indique si des destinations ont été trouvées
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
        /// Récupère les positions des destinations de chaque agent en parallèle
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
            // si la zone est sur le même étage que lui.

            if (agentTarget.FloorSurfaceIDs.x == agentState.FloorSurfaceIDs.x)
            {
                this.DestinationsWereFoundWO[index] = true;
                this.DestinationsWO[index] = agentTarget.Centroid;
                return;
            }

            NativeList<int> eligibleEZSurfaces = new(this.EZSurfacesRO.Length, Allocator.Temp);

            #region Recherche de points d'accès valides

            // Crée une liste de toutes les issues du bâtiment empruntables pour cet agent

            for (int i = 0; i < this.EZSurfacesRO.Length; i++)
            {
                Entity e = this.EZSurfacesRO[i];
                SurfacePositionCD pos = this.SurfacesPosesLookup[e];
                EZSurfaceMetadatasCD meta = this.EZMetadatasLookup[e];
                DynamicBuffer<SurfaceProfilesBE> profiles = this.ProfilesLookup[e];
                DynamicBuffer<EZSurfaceDestinationsBE> destinations = this.DestinationsLookup[e];

                // Si la zone est active et sur le même étage que l'agent

                if (meta.ZoneIsActive && pos.FloorSurfaceIDs.x == agentState.FloorSurfaceIDs.x)
                {
                    // La surface n'est éligible que si elle est empruntable par ce profil.
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
                        // Si elle accepte l'agent, on regarde ensuite si elle mène vers l'étage de la destination

                        for (int j = 0; j < destinations.Length; j++)
                        {
                            // On regarde pour chaque surface laquelle possède le GUID de destination
                            // et se trouve à l'étage de la cible de l'agent

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

            // On prend une entrée au hasard et on y place l'agent au début de la simulation

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
    /// Récupère les points de départ et d'arrivée de chaque agent
    /// </summary>
    [BurstCompile]
    private partial struct GetStartEndPositionsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Les agents à évaluer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les infos de chaque étage
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> FloorInfosRO;

        /// <summary>
        /// Indique si des destinations ont été trouvées
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
        /// Les propriétés des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentPropertiesCD> AgentPropertiesLookup;

        /// <summary>
        /// Les états internes des agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentStateCD> AgentStateLookup;

        /// <summary>
        /// Les positions de toutes les surfaces
        /// </summary>
        [ReadOnly]
        public ComponentLookup<SurfacePositionCD> SurfacesPosesLookup;

        /// <summary>
        /// Les nodes de chaque étage
        /// </summary>
        [ReadOnly]
        public BufferLookup<FloorInfoNodeBE> FloorNodesLookup;

        /// <summary>
        /// Les points de départ pour chaque agent
        /// </summary>
        [WriteOnly]
        public NativeArray<float3> StartPosesWO;

        /// <summary>
        /// Les points d'arrivée pour chaque agent
        /// </summary>
        [WriteOnly]
        public NativeArray<float3> EndPosesWO;

        #endregion

        #region Fonctions privées

        /// <summary>
        /// Récupère les points de départ et d'arrivée de chaque agent
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
    /// Vide les précédents chemins des agents
    /// </summary>
    [BurstCompile]
    private partial struct ClearAgentsPaths : IJobEntity
    {
        #region Fonctions publiques

        /// <summary>
        /// Vide les précédents chemins des agents
        /// </summary>
        /// <param name="agent">L'agent à réinitialiser</param>
        [BurstCompile]
        public void Execute(AgentFindPathAspect agent)
        {
            agent.ClearPath();
        }

        #endregion
    }

    /// <summary>
    /// Assigne un nouveau chemin à chaque agent en parallèle
    /// </summary>
    [BurstCompile]
    private partial struct AssignAgentsPathsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Pour créer les entités des chemins en parallèle
        /// </summary>
        public EntityCommandBuffer.ParallelWriter Ecb;

        /// <summary>
        /// Les agents à évaluer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les chemins calculés
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> PathsRO;

        /// <summary>
        /// Indique si des destinations on pu être trouvées
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
        /// Assigne un nouveau chemin à chaque agent en parallèle
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
    /// Détruit les agents sans destination
    /// et passe les autres à l'AgentMoveSystem
    /// </summary>
    [BurstCompile]
    private partial struct ResolveAgentsJob : IJobParallelFor
    {
        #region Variables d'instance

        /// <summary>
        /// Pour créer les entités des chemins en parallèle
        /// </summary>
        public EntityCommandBuffer.ParallelWriter Ecb;

        /// <summary>
        /// Les agents à évaluer
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> AgentsRO;

        /// <summary>
        /// Les zones de travail du bâtiment
        /// </summary>
        [ReadOnly]
        public NativeArray<Entity> POISurfacesRO;

        /// <summary>
        /// Indique si des destinations on pu être trouvées
        /// </summary>
        [ReadOnly]
        public NativeArray<bool> DestinationsWereFoundRO;

        /// <summary>
        /// Les propriétés de tous les agents
        /// </summary>
        [ReadOnly]
        public ComponentLookup<AgentPropertiesCD> AgentsPropertiesLookup;

        /// <summary>
        /// Les propriétés de tous les agents
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
        /// Détruit les agents sans destination
        /// </summary>
        /// <param name="index">La position de l'agent dans la liste</param>
        [BurstCompile]
        public void Execute(int index)
        {
            Entity agentE = this.AgentsRO[index];

            // On se débarrasse de l'agent s'il n'a pas de destination,
            // et on libère la place qui lui a été assignée

            if (Hint.Unlikely(!this.DestinationsWereFoundRO[index]))
            {
                AgentPropertiesCD agentProperties = this.AgentsPropertiesLookup[agentE];
                AgentStateCD agentState = this.AgentsStatesLookup[agentE];
                SurfacePositionCD agentTarget = this.AgentsTargetsLookup[agentE];

                // On récupère l'entité de sa destination

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

                        // Réassigne les components à l'entité pour sauvegarder les changements

                        this.Ecb.SetComponent(index, poiE, slots);
                        this.Ecb.SetComponent(index, poiE, counter);

                        break;
                    }
                }

                this.EnableAgentDestroyState(index, agentE, this.Ecb);

#if UNITY_EDITOR
                UnityEngine.Debug.Log($"Erreur : Le bâtiment n'a aucune ezSurface permettant à l'agent \"{agentProperties.Name}\" d'atteindre sa destination à l'étage {agentTarget.FloorSurfaceIDs}. Veuillez modifier le fichier dat.");
#endif

                return;
            }

            // Si tout va bien, on lance son processus de déplacement

            this.EnableAgentMoveState(index, agentE, this.Ecb);
        }

        #endregion

        #region Fonctions privées

        /// <summary>
        /// Libère ou assigne une place de la zone de travail
        /// </summary>
        /// <param name="slots">Les salles libres de chaque zone</param>
        /// <param name="counter">Les compteurs de salles libres de chaque zone</param>
        /// <param name="occupationType">L'occupation concernée</param>
        /// <param name="agentType">Le type de l'agent (nomade ou sédentaire)</param>
        /// <param name="agentGender">TRUE : F ; FALSE : H</param>
        private void FreePOI(ref POISurfaceFreeSlotsCD slots,
                             ref POISurfaceFreeSlotsCounterCD counter,
                             OccupationType occupationType,
                             ProfileType agentType,
                             bool agentGender)
        {
            switch (occupationType)
            {
                case OccupationType.Réunion:
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
        /// Arrête la recherche de chemins
        /// et désactive l'agent
        /// </summary>
        /// <param name="sortKey">L'id de l'agent</param>
        /// <param name="agentE">L'entité à désactiver</param>
        /// <param name="ecb">Permet de synchroniser les changements</param>
        private void EnableAgentDestroyState(int sortKey, Entity agentE, EntityCommandBuffer.ParallelWriter ecb)
        {
            ecb.SetComponentEnabled<AgentFindPathCD>(sortKey, agentE, false);
            ecb.SetComponentEnabled<AgentDestroyCD>(sortKey, agentE, true);
        }

        /// <summary>
        /// Arrête la recherche de chemins
        /// et lance le processus de déplacement de l'agent
        /// </summary>
        /// <param name="sortKey">L'id de l'agent</param>
        /// <param name="agentE">L'entité à désactiver</param>
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