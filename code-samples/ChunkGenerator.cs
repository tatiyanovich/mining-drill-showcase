using System.Collections.Generic;
using Code.Common.Utilities;
using Code.Gameplay.Environment.Services;
using Code.Gameplay.HarvestableGeneration.Configs;
using Code.Gameplay.HarvestableGeneration.Data;
using Code.Gameplay.Harvesting.Configs;
using Code.Gameplay.Harvesting.Data;
using Code.Gameplay.Stats;
using Code.Gameplay.Stats.Services;
using UnityEngine;
using Zenject;
using Random = System.Random;

namespace Code.Gameplay.HarvestableGeneration.Services
{
	public sealed class ChunkGenerator : IChunkGenerator
	{
		private readonly IStatsService _stats;
		private readonly IPlanetVisualsConfigsService _visuals;

		private readonly List<HarvestableFootprint> _footprints = new(32);
		private readonly List<HarvestableFootprint> _clusters = new(4);
		private readonly List<HarvestablePlacement> _harvestables = new(32);

		private ChunkGenerationStats _generationStats;

		private const int ClusterCountChannel = 0;
		private const int ScatteredCountChannel = 1;
		private const int ClusterFillChannel = 2;

		public IReadOnlyList<HarvestableFootprint> Footprints => _footprints;
		public IReadOnlyList<HarvestableFootprint> Clusters => _clusters;
		public IReadOnlyList<HarvestablePlacement> Harvestables => _harvestables;
		public ChunkGenerationStats GenerationStats => _generationStats;

		public ChunkGenerator(IStatsService stats) : this(stats, null)
		{
		}

		[Inject]
		public ChunkGenerator(IStatsService stats, IPlanetVisualsConfigsService visuals)
		{
			_stats = stats;
			_visuals = visuals;
		}

		public void Generate(ChunkGenerationRequest request)
		{
			_footprints.Clear();
			_clusters.Clear();
			_harvestables.Clear();
			_generationStats.Clear();

			GenerateClusters(request);
			GenerateScattered(request);
		}

		public bool TryGenerateCoreRock(
			ChunkGenerationRequest request,
			IReadOnlyList<HarvestableFootprint> placed,
			out HarvestablePlacement placement)
		{
			placement = default;

			float chance = _stats.GetValue(StatTypeId.CoreRockChance, request.Planet.CoreRockChance);

			if (chance <= 0f)
				return false;

			if (request.Random.NextDouble() > chance)
				return false;

			_generationStats.RequestedCoreRocks++;

			HarvestableRoll roll = HarvestableRoll.Roll(request.Planet.CoreRock, Skin(request, request.Planet.CoreRock), request.Random);

			if (request.Placement.TryFindPosition(roll.Radius, placed, out Vector3 position) == false)
				return false;

			_generationStats.PlacedCoreRocks++;

			placement = new HarvestablePlacement(roll, position, HarvestableSpawnRequest.NotGenerated);

			return true;
		}

		private void GenerateClusters(ChunkGenerationRequest request)
		{
			WeightedTable<HarvestableClusterConfig> table = request.Planet.ClusterTable;

			if (table.Entries.Count == 0)
				return;

			float baseCount = RollBaseCount(
				request.Config.MinClustersPerChunk,
				request.Config.MaxClustersPerChunk,
				request);

			int clusterCount = Round(
				_stats.GetValue(StatTypeId.HarvestableDensity, baseCount * request.Profile.Clusters),
				request,
				ClusterCountChannel);

			_generationStats.RequestedClusters += clusterCount;

			for (int index = 0; index < clusterCount; index++)
			{
				HarvestableClusterConfig cluster = WeightedPick.Pick(table, WeightOf, request.Random.NextDouble());
				float radius = RollRange(cluster.RadiusRange, request.Random) * VeinSpread(request);

				if (request.Placement.TryFindClusterCenter(radius, out Vector3 center) == false)
					continue;

				_clusters.Add(new HarvestableFootprint(center, radius));
				_generationStats.PlacedClusters++;

				FillCluster(new ClusterSpawnRequest(cluster, center, radius, index), request);
			}
		}

		private void FillCluster(ClusterSpawnRequest spawn, ChunkGenerationRequest request)
		{
			Vector2Int countRange = spawn.Config.CountRange;

			float baseCount = RollBaseCount(countRange.x, countRange.y, request);

			int count = Round(
				baseCount * request.Profile.ClusterFill,
				request,
				ClusterFillChannel + spawn.Index);

			_generationStats.RequestedHarvestables += count;

			for (int index = 0; index < count; index++)
			{
				HarvestableRoll roll = RollHarvestable(spawn.Config.SpawnTable, request);
				ClusterPlacementRequest placement = new(spawn, roll.Radius);

				if (request.Placement.TryFindPositionInDisc(placement, _footprints, out Vector3 position) == false)
					continue;

				Place(new HarvestablePlacement(roll, position, _footprints.Count), request);
			}
		}

		private void GenerateScattered(ChunkGenerationRequest request)
		{
			WeightedTable<HarvestableConfig> table = request.Planet.ScatteredTable;

			if (table.Entries.Count == 0)
				return;

			float baseCount = RollBaseCount(
				request.Config.MinScatteredPerChunk,
				request.Config.MaxScatteredPerChunk,
				request);

			int count = Round(
				_stats.GetValue(StatTypeId.HarvestableDensity, baseCount * request.Profile.Scattered),
				request,
				ScatteredCountChannel);

			_generationStats.RequestedHarvestables += count;

			for (int index = 0; index < count; index++)
			{
				HarvestableRoll roll = RollHarvestable(table, request);

				if (request.Placement.TryFindPosition(roll.Radius, _footprints, out Vector3 position) == false)
					continue;

				Place(new HarvestablePlacement(roll, position, _footprints.Count), request);
			}
		}

		private void Place(HarvestablePlacement placement, ChunkGenerationRequest request)
		{
			_footprints.Add(new HarvestableFootprint(placement.Position, placement.Roll.Radius));
			_generationStats.PlacedHarvestables++;

			if (request.MinedSpawnIndices.Contains(placement.SpawnIndex))
				return;

			_harvestables.Add(placement);
		}

		private HarvestableRoll RollHarvestable(WeightedTable<HarvestableConfig> table, ChunkGenerationRequest request)
		{
			HarvestableConfig config = WeightedPick.Pick(table, WeightOf, request.Random.NextDouble());

			return HarvestableRoll.Roll(config, Skin(request, config), request.Random);
		}

		private IHarvestableSkin Skin(ChunkGenerationRequest request, HarvestableConfig config)
		{
			return _visuals == null ? config : _visuals.GetHarvestableSkin(request.Planet.PlanetId, config);
		}

		private float RollBaseCount(int min, int max, ChunkGenerationRequest request)
		{
			return request.Random.Next(Mathf.Min(min, max), Mathf.Max(min, max) + 1);
		}

		private int Round(float count, ChunkGenerationRequest request, int channel)
		{
			if (count <= 0f)
				return 0;

			int whole = Mathf.FloorToInt(count);
			float remainder = count - whole;

			if (ChunkMath.DensityRoll(request.Config.RandomSeed, request.ChunkKey, channel) < remainder)
				whole++;

			return whole;
		}

		private static float VeinSpread(ChunkGenerationRequest request)
		{
			return Mathf.Sqrt(request.Profile.ClusterFill);
		}

		private float WeightOf<T>(WeightedEntry<T> entry)
		{
			return entry.WeightStat == StatTypeId.Unknown
				? entry.Weight
				: _stats.GetValue(entry.WeightStat, entry.Weight);
		}

		private static float RollRange(Vector2 range, Random random)
		{
			float min = Mathf.Min(range.x, range.y);
			float max = Mathf.Max(range.x, range.y);

			return (float)(min + random.NextDouble() * (max - min));
		}
	}
}
