import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../../src/TFlexDrawingService.Api/wwwroot/xizi-option-rules.js', import.meta.url), 'utf8');
const { xiziArdCode, isXiziManualOption, xiziConfigurationInput, xiziModelForTemplateId, xiziTemplateForSeries, xiziTemplateRestrictionFields, xiziValidationRuleFields } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
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
  assert.equal(xiziConfigurationInput(template, 'UN-Victor MRL', 1000, 1.75,
    { AA: 2100, BB: 1100, TR: 27900, stops: 10 }, false, 'CO', false)?.$CARTYPE_MENU,
    '13X / 1000 / 2100×1100');
  assert.equal(xiziConfigurationInput(template, 'UN-Victor MRL', 1000, 1, { AA: 2100, BB: 1600 }, false, 'CO', false), null);
});
test('Series resolves only to its explicit XIZI template', () => {
  const byId = new Map(templates.map(template => [template.id, template]));
  assert.equal(xiziTemplateForSeries('UN-Victor MRL', byId)?.id, 'un_victor_mrl');
  assert.equal(xiziTemplateForSeries('UN-Victor MRL(T)', byId)?.id, 'un_victor_mrl_t');
  assert.equal(xiziTemplateForSeries('UN-Victor R', byId)?.id, 'un_victor_r');
  assert.equal(xiziTemplateForSeries('unknown', byId), null);
  assert.equal(xiziModelForTemplateId('un_victor_r'), 'UN-Victor R');
  assert.equal(xiziModelForTemplateId('un_victor_mrl_t'), 'MRL-T');
  assert.equal(xiziModelForTemplateId('unknown'), null);
});
test('MRL rules map form dimensions and retain rules for template defaults', () => {
  for (const id of ['un_victor_mrl', 'un_victor_mrl_t']) {
    const template = templates.find(item => item.id === id);
    const rules = xiziValidationRuleFields(template);
    assert.ok(Object.values(rules).flat().includes('HL6'));
    assert.ok(Object.values(rules).flat().includes('JJ'));
    const input = xiziConfigurationInput(template, 'UN-Victor MRL', 1000, 1, { AA: 1100, BB: 2100, TR: 27900, stops: 10, HL: 2200, JJ: 900, HH: 2000, OH: 5300, PD: 1900, AH: 1800, BH: 2700 }, false, 'CO', false);
    assert.equal(Object.keys(rules).length, template.validationRules.length);
    const restrictionFields = xiziTemplateRestrictionFields(template);
    assert.equal(input.DOP, 350);
    assert.equal(input.HL6, undefined);
    assert.equal(input.$CWTLOC, '13');
    assert.equal(restrictionFields.doorAxisDefault, template.parameters.find(parameter => parameter.name === 'HL6').defaultValue);
    assert.equal(restrictionFields.doorOffsetIsNumeric, true);
    assert.equal(restrictionFields.counterweightName, '$CWTLOC');
    assert.equal(restrictionFields.counterweightIsMenu, true);
    assert.equal(restrictionFields.counterweightLabels['13'], 'Слева');
    const withRestrictions = xiziConfigurationInput(template, 'UN-Victor MRL', 1000, 1, { AA: 1100, BB: 2100, TR: 27900, stops: 10, HL: 2200, JJ: 900, HH: 2000, OH: 5300, PD: 1900, AH: 1800, BH: 2700 }, false, 'CO', false, { HL6: 1010, DOP: 150, $CWTLOC: '13' });
    assert.equal(withRestrictions.HL6, 1010);
    assert.equal(withRestrictions.DOP, 150);
    assert.equal(withRestrictions.$CWTLOC, '13');
    assert.equal(input.CH, 2200);
    assert.equal(input.OP, 900);
    assert.equal(input.OPH, 2000);
    assert.equal(input.$CWT, 'WOSAF');
    assert.equal(input.$CWT_MENU, undefined);
  }
});
test('Pricing form saves, restores and validates the exact template restriction fields', async () => {
  const pricing = await readFile(new URL('../../src/TFlexDrawingService.Api/wwwroot/pricing.js', import.meta.url), 'utf8');
  const html = await readFile(new URL('../../src/TFlexDrawingService.Api/wwwroot/pricing.html', import.meta.url), 'utf8');
  for (const id of ['xiziDoorAxisOffsetInput', 'xiziDoorOffsetSelect', 'xiziCounterweightLocationSelect', 'xiziCounterweightLocationInput']) {
    assert.match(html, new RegExp(`id="${id}"`, 'u'));
  }
  for (const key of ['Door Axis Offset', 'Door Offset', 'Counterweight Location']) {
    assert.ok(pricing.includes(`"${key}"`), `${key} is missing from pricing save/restore wiring`);
  }
  assert.match(pricing, /HL6: xiziDoorAxisOffsetInput\.value\.trim\(\) === "" \? undefined : numberValue\(xiziDoorAxisOffsetInput, NaN\)/u);
  assert.match(pricing, /DOP: xiziDoorOffsetSelect\.value === "" \? undefined : numberValue\(xiziDoorOffsetSelect, NaN\)/u);
  assert.match(pricing, /const counterweight = get\("\$CWTLOC_MENU", "\$CWTLOC"\)/u);
  assert.match(pricing, /\["HL6", xiziDoorAxisOffsetInput, "HL6"\]/u);
  assert.match(pricing, /\["DOP", xiziDoorOffsetSelect, "DOP"\]/u);
  assert.match(pricing, /if \(state\.catalog\) renderXiziTemplateRestrictionControls\(\)/u);
});
test('Absent technical parameters are not forced into a different XIZI model', () => {
  const mrl = templates.find(item => item.id === 'un_victor_mrl');
  const template = {
    parameters: [mrl.parameters.find(parameter => parameter.name === '$CARTYPE_MENU'), {
      name: '$CWTLOC_MENU', allowedValues: ['12', '13', '24'], defaultValue: '12'
    }]
  };
  const input = xiziConfigurationInput(template, 'UN-Victor R', 1000, 1,
    { AA: 1100, BB: 2100, TR: 27900, stops: 10 }, false, 'CO', false,
    { HL6: 1050, DOP: 150, $CWTLOC: '12' });
  assert.equal(input.HL6, undefined);
  assert.equal(input.DOP, undefined);
  assert.equal(input.$CWTLOC, undefined);
  assert.equal(input.$CWTLOC_MENU, '12');
});

test('R car choices distinguish duplicate dimensions by counterweight location', () => {
  const template = { parameters: [
    { name: '$CARTYPE_MENU', allowedValues: [
      '08D /  630 / 1100×1400 / Сбоку', '08D /  630 / 1100×1400 / Сзади'
    ] },
    { name: '$CWTLOC_MENU', allowedValues: ['12', '13', '24'], defaultValue: '12' },
    { name: '$CWT_MENU', allowedValues: ['WOSAF', 'WSAFE'] },
    { name: '$HAND', allowedValues: ['RIGHT', 'LEFT', 'CENTR'] },
    { name: 'DOP', defaultValue: 350 }
  ] };
  const values = { AA: 1100, BB: 1400, TR: 27900, stops: 10, HL: 2200, OH: 5300, PD: 1900, AH: 1800, BH: 2700, JJ: 900, HH: 2000 };
  const rear = xiziConfigurationInput(template, 'UN-Victor R', 630, 1, values, false, 'TLD', false, { $CWTLOC: '12', DOP: 417 });
  const side = xiziConfigurationInput(template, 'UN-Victor R', 630, 1, values, false, 'TLD', true, { $CWTLOC: '24', DOP: 525 });
  assert.equal(rear.$CARTYPE_MENU, '08D /  630 / 1100×1400 / Сзади');
  assert.equal(side.$CARTYPE_MENU, '08D /  630 / 1100×1400 / Сбоку');
  assert.equal(side.$CWT_MENU, 'WSAFE');
  assert.equal(rear.DOP, 417);
  assert.equal(side.DOP, 525);
  assert.equal(rear.$CWT_MENU, 'WOSAF');
  assert.equal(side.$CWT_MENU, 'WSAFE');
  assert.equal(rear.HD, 2700);
  assert.equal(rear.$HAND, 'LEFT');
});

test('New R inputs map to user-facing validation controls', () => {
  const fields = xiziValidationRuleFields({ validationRules: [{ name: 'r', fieldNames: ['HD', '$OPH_CH', '$CWTLOC_MENU', '$HAND', 'DOP', 'NBENT_MENU', 'V_MENU'] }] });
  assert.deepEqual(fields.r, ['BH', 'HH', 'Counterweight Location', 'Door Opening', 'Door Offset', 'Doors', 'Speed']);
});
