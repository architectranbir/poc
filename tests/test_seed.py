import json,pathlib,unittest
base=pathlib.Path(__file__).resolve().parents[1]/'seed'
class SeedTests(unittest.TestCase):
 def test_projects(self):
  d=json.loads((base/'projects.json').read_text());self.assertEqual(len(d),5);self.assertEqual(len({x['id'] for x in d}),5)
 def test_wip(self):
  p={x['id'] for x in json.loads((base/'projects.json').read_text())};d=json.loads((base/'wip-lines.json').read_text());self.assertGreater(len(d),40);self.assertTrue(all(x['projectId'] in p for x in d));self.assertTrue(all(x['amount']>=0 for x in d))
 def test_thresholds(self):
  p=json.loads((base/'policy-thresholds.json').read_text());self.assertEqual(p['financeBpThresholds']['Advisory'],20000);self.assertEqual(p['thresholdComparator'],'STRICT_GREATER_THAN')
if __name__=='__main__':unittest.main()
