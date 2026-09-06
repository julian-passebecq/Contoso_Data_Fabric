from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class ScenarioDefinition:
    id: str
    title: str
    implemented: bool
    tables: tuple[str, ...]
    default_features: tuple[str, ...]


SCENARIOS = {
    "sales-bi": ScenarioDefinition(
        id="sales-bi",
        title="Sales & BI",
        implemented=True,
        tables=("customer", "store", "product", "date", "currencyexchange", "sales", "orders", "orderrows"),
        default_features=("multiCurrency",),
    ),
    "customer-experience": ScenarioDefinition(
        id="customer-experience",
        title="Customer Experience & Delivery",
        implemented=False,
        tables=("shipment", "shipment_event", "return", "support_ticket", "review"),
        default_features=("returns", "shipmentDelays", "supportTickets", "reviews"),
    ),
    "data-quality": ScenarioDefinition(
        id="data-quality",
        title="Data Engineering / Quality",
        implemented=False,
        tables=("sales", "customer"),
        default_features=("duplicates", "cdc", "lateArrivals", "scd2", "quarantine"),
    ),
    "ml-dissatisfaction": ScenarioDefinition(
        id="ml-dissatisfaction",
        title="ML Customer Dissatisfaction",
        implemented=False,
        tables=("customer", "shipment", "support_ticket", "return", "review"),
        default_features=("is_dissatisfied_14d",),
    ),
}
