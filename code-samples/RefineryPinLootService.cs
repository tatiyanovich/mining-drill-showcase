using System;
using System.Collections.Generic;
using Code.Common.Extensions;
using Code.Gameplay.Income.Services;
using Code.Gameplay.Items;
using Code.Gameplay.Refinery.Configs;
using Code.Gameplay.Refinery.Data;
using Code.Gameplay.SkillTree.Queries;
using Code.Gameplay.Skills;
using Code.Gameplay.Stats;
using Code.Gameplay.Stats.Services;
using Code.Infrastructure.EntityComponentSystem.Factories;
using Code.Infrastructure.Randomization;
using Entitas;
using UnityEngine;

namespace Code.Gameplay.Refinery.Services
{
	public sealed class RefineryPinLootService : IRefineryPinLootService
	{
		private sealed class StoneState
		{
			public readonly string Id;
			public readonly HashSet<int> PaidPins;
			public readonly HashSet<int> TouchedPins;

			public StoneState(string id, IEnumerable<int> paidPins)
			{
				Id = id;
				PaidPins = paidPins == null ? new HashSet<int>() : new HashSet<int>(paidPins);
				TouchedPins = new HashSet<int>(PaidPins);
			}
		}

		private readonly IRefineryConfigsService _refineryConfigsService;
		private readonly IStatsService _statsService;
		private readonly ISkillTreeQuery _skillTreeQuery;
		private readonly IRandomService _randomService;
		private readonly IIncomeService _incomeService;
		private readonly IEntityFactory _entityFactory;
		private readonly IGroup<GameEntity> _sessions;
		private readonly Dictionary<int, StoneState> _stones = new();
		private readonly HashSet<int> _legacySessions = new();
		private readonly List<GameEntity> _buffer = new(1);

		public event Action<RefineryPinContactDto> OnPinContact;

		public RefineryPinLootService(
			GameContext game,
			IRefineryConfigsService refineryConfigsService,
			IStatsService statsService,
			ISkillTreeQuery skillTreeQuery,
			IRandomService randomService,
			IIncomeService incomeService,
			IEntityFactory entityFactory)
		{
			_refineryConfigsService = refineryConfigsService;
			_statsService = statsService;
			_skillTreeQuery = skillTreeQuery;
			_randomService = randomService;
			_incomeService = incomeService;
			_entityFactory = entityFactory;

			_sessions = game.GetGroup(GameMatcher
				.AllOf(
					GameMatcher.RefinerySession,
					GameMatcher.TotalRawValue,
					GameMatcher.TotalStoneCount,
					GameMatcher.EarnedMoney)
				.NoneOf(
					GameMatcher.RefineryCompleted,
					GameMatcher.Destructed));
		}

		public bool TryDropPinLoot(GameEntity stone, int pinId, Vector3 position, float impactSpeed)
		{
			if (stone == null || stone.isRefineryStone == false || stone.isStoneProcessed || stone.isDestructed)
				return false;

			if (_sessions.count == 0)
				return false;

			GameEntity session = _sessions.GetEntities(_buffer)[0];
			StoneState state = State(stone);
			bool isNewPair = state.TouchedPins.Add(pinId);
			int reward = 0;

			if (_skillTreeQuery.CurrentLevel(SkillTypeId.DeepScan) > 0)
				reward = IsLegacySession(session)
					? LegacyReward(session, impactSpeed)
					: PairReward(stone, state, pinId);

			int paid = reward > 0
				? Pay(session, new RefineryPinPaymentDto(state.Id, pinId, position), reward)
				: 0;

			OnPinContact?.Invoke(new RefineryPinContactDto(
				stone.Id,
				stone.StoneBaseValue,
				pinId,
				impactSpeed,
				isNewPair,
				paid));

			return paid > 0;
		}

		public void RegisterStone(GameEntity stone, string stoneId = null, IEnumerable<int> paidPinIds = null)
		{
			_stones[stone.Id] = new StoneState(stoneId ?? Guid.NewGuid().ToString("N"), paidPinIds);
		}

		public void ForgetStone(GameEntity stone) => _stones.Remove(stone.Id);

		public string StoneId(GameEntity stone) => State(stone).Id;

		public List<int> PaidPinIds(GameEntity stone) => new(State(stone).PaidPins);

		public void MarkLegacySession(GameEntity session) => _legacySessions.Add(session.Id);

		public bool IsLegacySession(GameEntity session) => _legacySessions.Contains(session.Id);

		private StoneState State(GameEntity stone)
		{
			if (_stones.TryGetValue(stone.Id, out StoneState state))
				return state;

			RegisterStone(stone);

			return _stones[stone.Id];
		}

		private int PairReward(GameEntity stone, StoneState state, int pinId)
		{
			float share = _statsService.GetValue(StatTypeId.PinLootShare, 0f);

			if (share <= 0f || state.PaidPins.Add(pinId) == false)
				return 0;

			return Mathf.Max(1, Mathf.RoundToInt(stone.StoneBaseValue * share));
		}

		private int LegacyReward(GameEntity session, float impactSpeed)
		{
			RefineryConfig config = _refineryConfigsService.Config;

			if (impactSpeed < config.PinLootMinImpactSpeed || session.TotalStoneCount <= 0)
				return 0;

			if (_randomService.Chance(LegacyChance()) == false)
				return 0;

			return Mathf.Max(
				config.MinStoneReward,
				Mathf.RoundToInt((float)session.TotalRawValue / session.TotalStoneCount * config.PinLootShare));
		}

		private float LegacyChance()
		{
			return _skillTreeQuery.CurrentLevel(SkillTypeId.DeepScan) switch
			{
				1 => 0.02f,
				2 => 0.045f,
				_ => 0.08f,
			};
		}

		private int Pay(GameEntity session, RefineryPinPaymentDto payment, int reward)
		{
			int paid = _incomeService.Earn(ItemTypeId.Money, reward);
			session.ReplaceEarnedMoney(session.EarnedMoney.SaturatingAdd(paid));

			_entityFactory.Event()
				.AddRefineryPinLootEvent(paid, payment.Position, session.Id, payment.StoneId, payment.PinId, "PinLoot");

			return paid;
		}
	}
}
