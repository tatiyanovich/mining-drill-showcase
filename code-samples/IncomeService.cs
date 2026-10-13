using System;
using System.Collections.Generic;
using Code.Gameplay.Income.Configs;
using Code.Gameplay.Inventory.Services;
using Code.Gameplay.Items;
using Code.Gameplay.SkillTree.Queries;

namespace Code.Gameplay.Income.Services
{
	public sealed class IncomeService : IIncomeService
	{
		private readonly Dictionary<ItemTypeId, double> _carries = new();

		private readonly IInventoryService _inventoryService;
		private readonly IIncomeConfigsService _incomeConfigsService;
		private readonly ISkillTreeQuery _skillTreeQuery;

		public IncomeService(
			IInventoryService inventoryService,
			IIncomeConfigsService incomeConfigsService,
			ISkillTreeQuery skillTreeQuery)
		{
			_inventoryService = inventoryService;
			_incomeConfigsService = incomeConfigsService;
			_skillTreeQuery = skillTreeQuery;
		}

		public int Earn(ItemTypeId itemTypeId, int amount)
		{
			int credited = Scale(itemTypeId, amount, out double carry);

			_carries[itemTypeId] = carry;
			_inventoryService.AddCurrency(itemTypeId, credited);

			return credited;
		}

		public int Preview(ItemTypeId itemTypeId, int amount) => Scale(itemTypeId, amount, out _);

		public float Multiplier(ItemTypeId itemTypeId)
		{
			float full = FullMultiplier(itemTypeId);

			if (full <= 1f)
				return 1f;

			return 1f + (full - 1f) * RampShare(_incomeConfigsService.Config);
		}

		private int Scale(ItemTypeId itemTypeId, int amount, out double carry)
		{
			_carries.TryGetValue(itemTypeId, out double previous);
			carry = previous;

			if (amount <= 0)
				return amount;

			double scaled = previous + amount * (double)Multiplier(itemTypeId);
			double whole = Math.Floor(scaled);

			if (whole >= int.MaxValue)
			{
				carry = 0d;

				return int.MaxValue;
			}

			carry = scaled - whole;

			return (int)whole;
		}

		private float FullMultiplier(ItemTypeId itemTypeId)
		{
			IncomeProfileConfig config = _incomeConfigsService.Config;

			return itemTypeId switch
			{
				ItemTypeId.Money => config.MoneyMultiplier,
				ItemTypeId.Core => config.CoreMultiplier,
				_ => 1f,
			};
		}

		private float RampShare(IncomeProfileConfig config)
		{
			int score = _skillTreeQuery.GetCurrentScore();

			if (score < config.RampStartScore)
				return 0f;

			if (score >= config.RampEndScore)
				return 1f;

			return (float)(score - config.RampStartScore) / (config.RampEndScore - config.RampStartScore);
		}
	}
}
