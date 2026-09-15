import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../../src/TFlexDrawingService.Api/wwwroot/xizi-option-rules.js', import.meta.url), 'utf8');
const { xiziArdCode, isXiziManualOption, xiziConfigurationInput } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const { templates } = JSON.parse(await readFile(new URL('../../templates/templates.json', import.meta.url)));
test('Only the matching ARD is selectable at load and speed boundaries', () => {
  for (const [q, v, expected] of [[1000, 1, 'ARD_15'], [1050, 2, 'ARD_22'], [1050, 2.5, 'ARD_37'], [1275, 1.75, 'ARD_22_2'], [1275, 2, 'ARD_37_2']]) {
    assert.equal(xiziArdCode(q, v), expected);
    const choices = ['ARD_15', 'ARD_22', 'ARD_37', 'ARD_22_2', 'ARD_37_2'].filter(c => isXiziManualOption(c, 'UN-Victor MRL', q, v));
    assert.deepEqual(choices, [expected]);
  }
});
test('MR-only option, logistics and duplicate AC controls are excluded', () => {
  for (const series of ['UN-Victor MRL', 'UN-Victor MRL(T)', 'G3']) assert.equal(isXiziManualOption('CWT_SIDE', series, 1000, 1), false);
  assert.equal(isXiziManualOption('CWT_SIDE', 'UN-Victor R', 1000, 1), true);
  for (const code of ['CONTAINER_20GP','CONTAINER_40HQ','ACCOLDSMALL','ACCOLDLARGE','ACHEATSMALL','ACHEATLARGE','RCC']) assert.equal(isXiziManualOption(code, 'UN-Victor R', 1000, 1), false);
});
test('Cabin geometry is matched to actual model and load, with millimetres converted to metres', () => {
  const template = templates.find(t => t.id === 'un_victor_mrl');
  const input = xiziConfigurationInput(template, 'UN-Victor MRL', 1000, 1.75, { AA: 1100, BB: 2100, TR: 27900, stops: 10 }, false, 'CO', false);
  assert.equal(input.$R, '27.9');
  assert.equal(input.$CARTYPE_MENU, '13D / 1000 / 1100×2100');
  assert.equal(xiziConfigurationInput(template, 'UN-Victor MRL', 1000, 1, { AA: 2100, BB: 1600 }, false, 'CO', false), null);
});
