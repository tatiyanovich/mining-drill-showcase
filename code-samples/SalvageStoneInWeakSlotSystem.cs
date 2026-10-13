using System.Collections.Generic;
using Code.Gameplay.Stats;
using Code.Gameplay.Stats.Services;
using Entitas;

namespace Code.Gameplay.Refinery.Systems
{
	public sealed class SalvageStoneInWeakSlotSystem : IExecuteSystem
	{
		private readonly GameContext _game;

		private readonly IStatsService _statsService;

		private readonly IGroup<GameEntity> _triggerEnters;
		private readonly IGroup<GameEntity> _slots;
		private readonly IGroup<GameEntity> _salvageableStones;

		private readonly List<GameEntity> _buffer = new(16);

		public SalvageStoneInWeakSlotSystem(GameContext game, IStatsService statsService)
		{
			_game = game;
			_statsService = statsService;

			_triggerEnters = game.GetGroup(GameMatcher
				.AllOf(
					GameMatcher.TriggerEnter,
					GameMatcher.CollidedId,
					GameMatcher.ColliderOwnerId)
				.NoneOf(
					GameMatcher.Destructed));

			_slots = game.GetGroup(GameMatcher
				.AllOf(
					GameMatcher.RefinerySlot,
					GameMatcher.SlotMultiplier));

			_salvageableStones = game.GetGroup(GameMatcher
				.AllOf(
					GameMatcher.RefineryStone,
					GameMatcher.Rigidbody2D)
				.NoneOf(
					GameMatcher.StoneSalvaged,
					GameMatcher.StoneProcessed,
					GameMatcher.StonePending,
					GameMatcher.Destructed));
		}

		public void Execute()
		{
			float salvagedBelow = _statsService.GetValue(StatTypeId.SalvageSlotMultiplier, 0f);

			if (salvagedBelow <= 0f)
				return;

			foreach (GameEntity triggerEnter in _triggerEnters.GetEntities(_buffer))
			{
				GameEntity slot = _game.GetEntityWithId(triggerEnter.ColliderOwnerId);
				GameEntity stone = _game.GetEntityWithId(triggerEnter.CollidedId);

				if (slot == null || stone == null)
					continue;

				if (_slots.ContainsEntity(slot) == false || _salvageableStones.ContainsEntity(stone) == false)
					continue;

				if (slot.SlotMultiplier > salvagedBelow)
					continue;

				stone.isStoneSalvaged = true;
				stone.isStoneSalvageRequested = true;

				triggerEnter.isDestructed = true;
			}
		}
	}
}
