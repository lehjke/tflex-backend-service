"""Business invariants for combining the supplier workbook and reference project."""
import sys
import unittest
from copy import deepcopy
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from sync_xizi_excel import apply_supplement


class XiziPriceSupplementTests(unittest.TestCase):
    def catalog(self, options):
        return {"smec": {"keep": [123]}, "xizi": {
            "series": [], "basePrices": [], "doors": [], "decorations": [],
            "options": options, "localRequirements": [], "priceSource": {"kind": "excel"},
        }}

    def supplement(self, options):
        return {"source": {"kind": "reference-project"}, "options": options}

    def test_excel_zero_unavailable_and_tbd_are_not_missing_prices(self):
        for price in (0, -1, "TBD", 100):
            with self.subTest(price=price):
                original = {"code": "P", "price": price, "source": {"sheet": "Options", "cell": "B4"}}
                catalog = self.catalog([deepcopy(original)])
                apply_supplement(catalog, self.supplement([{"code": "P", "price": 999}]))
                self.assertEqual(original, catalog["xizi"]["options"][0])
                self.assertEqual({"keep": [123]}, catalog["smec"])

    def test_null_price_is_filled_and_reapplication_does_not_duplicate_it(self):
        catalog = self.catalog([{"code": "P", "price": None}])
        addition = {"code": "P", "price": 0, "source": {"kind": "reference-project"}}
        supplement = self.supplement([addition])
        apply_supplement(catalog, supplement)
        once = deepcopy(catalog)
        apply_supplement(catalog, supplement)
        self.assertEqual(once, catalog)
        self.assertEqual([addition], catalog["xizi"]["options"])

    def test_russia_requirement_is_added_once_to_automatic_requirements(self):
        catalog = self.catalog([])
        ladder = {"code": "Ladder", "category": "russia", "price": 200, "source": {"kind": "reference-project"}}
        supplement = self.supplement([ladder])
        apply_supplement(catalog, supplement)
        apply_supplement(catalog, supplement)
        self.assertEqual([], catalog["xizi"]["options"])
        self.assertEqual([ladder], catalog["xizi"]["localRequirements"])

    def test_refresh_updates_only_the_secondary_source_price(self):
        catalog = self.catalog([{"code": "P", "price": 200, "source": {"kind": "reference-project"}}])
        apply_supplement(catalog, self.supplement([{"code": "P", "price": 300, "source": {"kind": "reference-project"}}]))
        self.assertEqual(300, catalog["xizi"]["options"][0]["price"])

    def test_reference_priority_overrides_excel_and_preserves_excel_only_prices(self):
        for old in (0, -1, "TBD", 100):
            for new in (0, -1, 999):
                with self.subTest(old=old, new=new):
                    untouched = {"code": "EXCEL_ONLY", "price": -1}
                    catalog = self.catalog([{"code": "P", "price": old}, untouched])
                    supplement = self.supplement([{"code": "P", "price": new}])
                    supplement["priority"] = "reference-project"
                    apply_supplement(catalog, supplement)
                    self.assertEqual(new, catalog["xizi"]["options"][0]["price"])
                    self.assertEqual(untouched, catalog["xizi"]["options"][1])
                    self.assertEqual({"keep": [123]}, catalog["smec"])

    def test_reference_comfort_option_remains_an_existing_automatic_requirement(self):
        catalog = self.catalog([])
        catalog["xizi"]["localRequirements"] = [{"code": "VOICE", "price": 1}]
        supplement = self.supplement([{"code": "VOICE", "category": "comfort", "price": 240}])
        supplement["priority"] = "reference-project"
        apply_supplement(catalog, supplement)
        once = deepcopy(catalog)
        apply_supplement(catalog, supplement)
        self.assertEqual(once, catalog)
        self.assertEqual([], catalog["xizi"]["options"])
        self.assertEqual(240, catalog["xizi"]["localRequirements"][0]["price"])


if __name__ == "__main__":
    unittest.main()
