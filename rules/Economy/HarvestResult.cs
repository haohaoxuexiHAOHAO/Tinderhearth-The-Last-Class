namespace Tinderhearth.Rules.Economy;

/// <summary>一次收获收上来什么：几件，以及是不是长成时掷中变异的那一株。</summary>
/// <remarks>
/// 「变异」交给产出那一手，它就是变异品质；品质那一轴的档位已在册（<see cref="Progression.ItemCatalog"/>），
/// 收获时落成哪一档归设计仓台账 GP-161。
/// 没收成时是 <c>default</c>：0 件、不变异。
/// </remarks>
/// <param name="Count">收上来几件。收成了就至少 1 件。</param>
/// <param name="Mutated">收的是不是变异的那一株。</param>
public readonly record struct HarvestResult(int Count, bool Mutated);
