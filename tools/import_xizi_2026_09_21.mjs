import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const [rPath, mrlTPath, mrlPath] = process.argv.slice(2);
if (!rPath || !mrlTPath || !mrlPath) {
  throw new Error('Usage: node tools/import_xizi_2026_09_21.mjs <R inspection.json> <MRL T inspection.json> <MRL inspection.json>');
}

const catalogPath = path.resolve('templates/templates.json');
const catalog = JSON.parse(await readFile(catalogPath, 'utf8'));
const existing = new Map(catalog.templates.map(template => [template.id, template]));
const sources = [
  { id: 'un_victor_mrl', file: mrlPath, folder: 'UN-Victor MRL 21-09-2026', grb: 'UN-Victor MRL.grb' },
  { id: 'un_victor_mrl_t', file: mrlTPath, folder: 'UN-Victor MRL T 21-09-2026', grb: 'UN-Victor MRL T.grb' },
  { id: 'un_victor_r', file: rPath, folder: 'UN-Victor R 21-09-2026', grb: 'UN-Victor R.grb' }
];

const inputLabels = {
  HD: 'Шахта / Глубина шахты',
  '$CWTLOC_MENU': 'Противовес / Расположение',
  '$CWT_MENU': 'Противовес / Ловители',
  '$HAND': 'Двери / Направление открывания',
  V_MENU: 'Характеристики / Скорость (меню)',
  '$OPH_CH': 'Двери / Высота проема и кабины',
  '$dop_menu': 'Двери / Смещение проема'
};
const ruleFields = {
  err01: ['$R'], err02: ['$N'], err11: ['K'], err12: ['S'], err13: ['HW'],
  err16: ['HD', 'WTW'], err17: ['HD', 'WTW'], err18: ['HL6'], err19: ['HL6'],
  err001: ['$CARTYPE_MENU', 'OP']
};

function unquote(value) {
  const text = String(value ?? '').trim();
  if (text.startsWith('"') && text.endsWith('"')) {
    try { return JSON.parse(text); } catch { return text.slice(1, -1); }
  }
  return text;
}

function unique(values) {
  return [...new Set(values.map(unquote))];
}

function tableValues(database, column) {
  const index = database?.columns?.findIndex(item => item.name === column) ?? -1;
  return index < 0 ? [] : unique(database.rows.map(row => row[index]));
}

function sortedNumbers(values) {
  return unique(values).sort((left, right) => Number(left) - Number(right));
}

function defaultValue(variable, type) {
  const value = unquote(variable.value);
  if ((type === 'integer' || type === 'number') && value !== '' && Number.isFinite(Number(value))) {
    return Number(value);
  }
  return value;
}

function dependencyNames(expression, variableMap) {
  return [...String(expression || '').matchAll(/\$?[A-Za-z_][A-Za-z_0-9]*/gu)]
    .map(match => match[0]).filter(name => variableMap.has(name));
}

function errorRule(variable, oldRule, availableNames, isR) {
  const split = variable.expression.indexOf('? ERROR(');
  if (split < 0) return null;
  const name = variable.name;
  const fields = (ruleFields[name] || oldRule?.fieldNames || [])
    .filter(field => availableNames.has(field));
  if (isR && (name === 'err16' || name === 'err17')) {
    fields.length = 0;
    fields.push('HD');
  } else if (!isR && (name === 'err16' || name === 'err17')) {
    fields.length = 0;
    fields.push('WTW');
  }
  const messageMatch = variable.expression.match(/ERROR\("((?:\\.|[^"\\])*)"\)/u);
  return {
    name,
    expression: `(${variable.expression.slice(0, split).trim()}) ? 0 : 1`,
    message: messageMatch ? JSON.parse(`"${messageMatch[1]}"`) : oldRule?.message || name,
    fieldNames: fields
  };
}

function createTemplate(source, inspection) {
  if (inspection.rebuildWarnings?.length) throw new Error(`${source.id} has rebuild warnings`);
  if (!inspection.databases?.length) throw new Error(`${source.id} inspection has no databases`);
  const isR = source.id === 'un_victor_r';
  const reference = existing.get(isR ? 'un_victor_mrl' : source.id);
  const references = new Map([...reference.parameters, ...reference.calculatedVariables]
    .map(definition => [definition.name, definition]));
  const variables = new Map(inspection.variables.map(variable => [variable.name, variable]));
  const databases = new Map(inspection.databases.map(database => [database.name, database]));
  const controls = new Map();
  for (const control of inspection.controls) {
    if (!control.variable) continue;
    const list = controls.get(control.variable) || [];
    list.push(control);
    controls.set(control.variable, list);
  }

  function definition(variable, calculated = false) {
    const previous = references.get(variable.name) || {};
    const nativeValues = unique(variable.allowedValues || []);
    let allowedValues = nativeValues;
    if (variable.name === '$V') allowedValues = sortedNumbers(tableValues(databases.get('Speed'), 'V'));
    if (variable.name === 'OP') allowedValues = sortedNumbers(tableValues(databases.get('Doorwidth'), 'OP'));
    if (variable.name === 'OPH') allowedValues = sortedNumbers(tableValues(databases.get('Doorheight'), 'OPH'));
    if (variable.name === 'CH') allowedValues = sortedNumbers(tableValues(databases.get('Carheight'), 'CH'));
    if (variable.name === 'DOP') allowedValues = sortedNumbers(tableValues(databases.get('Dop'), 'DOP'));
    if (variable.name === '$DOOR_MENU' && !allowedValues.length) {
      allowedValues = unique(variables.get('$DOOR')?.allowedValues || []);
    }
    const numeric = !variable.isText && !variable.name.startsWith('$');
    const type = numeric ? (Number.isInteger(Number(variable.value)) ? 'integer' : 'number')
      : allowedValues.length ? 'enum' : 'string';
    let expression = variable.expression;
    if (isR && variable.name === 'HD_MIN') {
      expression = '$CWTLOC == "12" ? ((tol + ldt + 30 + cdt) + df + x_cwt + ($CWT == "WOSAF" ? 153 : 260)) : MAX(hd_min_cwt, NBENT * (tol + ldt + 30 + cdt) + CD + (NBENT == 1 ? 75 : 0))';
    }
    if (isR && variable.name === 'HD_MAX') {
      expression = '$CWTLOC == "12" ? ((ldt + 30 + cdt) + df + x_cwt + 500) : (NBENT == 1 ? 3500 : 2 * (tol + ldt + 30 + cdt) + CD + 50)';
    }
    const result = {
      name: variable.name,
      displayName: isR && inputLabels[variable.name]
        ? inputLabels[variable.name]
        : previous.displayName || variable.comment || variable.name,
      type,
      isRequired: calculated ? false : previous.isRequired ?? true,
      defaultValue: defaultValue(variable, type),
      expression
    };
    if (calculated) result.isReadOnly = true;
    if (previous.unit || variable.unit) result.unit = previous.unit || variable.unit;
    if (!calculated && previous.submitDefault === false) result.submitDefault = false;
    if (!calculated && previous.submitWhenDisabled) result.submitWhenDisabled = true;
    if (allowedValues.length) result.allowedValues = allowedValues;
    if (variable.name === '$CWTLOC' || variable.name === '$CWTLOC_MENU') {
      result.allowedValueLabels = { '12': 'Сзади', '13': 'Слева', '24': 'Справа' };
    } else if (previous.allowedValueLabels && allowedValues.length) {
      result.allowedValueLabels = previous.allowedValueLabels;
    }
    const levels = unique((controls.get(variable.name) || []).map(control => control.level)
      .filter(level => level && level !== '0' && level !== '1' && level !== '-1'));
    if (!calculated && levels.length === 1) result.levelExpression = levels[0];
    if (!calculated && levels.length > 1 && levels.includes(previous.levelExpression)) {
      result.levelExpression = previous.levelExpression;
    }
    if (variable.name === 'DOP') {
      result.description = 'Допустимое сочетание двери, кабины и смещения определяется таблицей Dop этого шаблона.';
    } else if (previous.description && !isR) {
      result.description = previous.description;
    }
    if (allowedValues.length && !allowedValues.includes(String(result.defaultValue))) {
      allowedValues.push(String(result.defaultValue));
    }
    return result;
  }

  const parameters = inspection.variables.filter(variable => variable.external && !variable.hidden && !variable.service)
    .map(variable => definition(variable));
  const availableNames = new Set(parameters.map(item => item.name));
  const oldRules = new Map(reference.validationRules.map(rule => [rule.name, rule]));
  const validationRules = inspection.variables
    .filter(variable => /^err\d+$/u.test(variable.name))
    .map(variable => errorRule(variable, oldRules.get(variable.name), availableNames, isR))
    .filter(rule => rule && rule.fieldNames.length);

  for (const oldRule of reference.validationRules) {
    if (!/^warn\d+$/u.test(oldRule.name) || !variables.has(oldRule.name)) continue;
    validationRules.push({ ...oldRule, fieldNames: isR ? oldRule.fieldNames.map(name => name === 'WTW' ? 'HD' : name) : oldRule.fieldNames });
  }
  if (isR) {
    validationRules.push({ name: 'r_door_height_700', expression: 'OP != 700 || OPH == 2000',
      message: 'При проеме 700 мм высота двери должна быть 2000 мм', fieldNames: ['OP', 'OPH'] });
  } else {
    const overhead = oldRules.get('overhead_max');
    if (overhead && variables.has('K_MAX')) validationRules.push(overhead);
  }

  const lookupTables = {};
  for (const database of inspection.databases) {
    lookupTables[database.name] = database.rows.map(row => Object.fromEntries(database.columns.map((column, index) => [
      column.name, column.type === 1 ? Number(row[index]) : String(row[index])
    ])));
  }
  const lookupRules = [
    ['Speed', 'DL', `Speed.DL == DL && Speed.V == V${isR ? ' && Speed.NBENT == NBENT' : ''}`,
      ['$CARTYPE_MENU', '$V', ...(isR ? ['NBENT_MENU'] : [])], 'Скорость недоступна для выбранной кабины'],
    ['Doorwidth', 'OP', `Doorwidth.CARTYPE == $CARTYPE && Doorwidth.DOOR == $DOOR && Doorwidth.OP == OP${isR ? '' : ' && Doorwidth.NBENT == NBENT'}`,
      ['$CARTYPE_MENU', isR ? '$HAND' : '$DOOR_MENU', 'OP', ...(!isR ? ['NBENT_MENU'] : [])], 'Ширина двери недоступна для выбранной кабины'],
    ['Dop', 'OP', `Dop.DOOR == $DOOR && Dop.CW == CW && Dop.OP == OP && Dop.DOP == DOP${isR ? '' : ' && Dop.NBENT == NBENT'}`,
      ['$CARTYPE_MENU', isR ? '$HAND' : '$DOOR_MENU', 'OP', 'DOP', ...(!isR ? ['NBENT_MENU'] : [])], 'Смещение дверного проема недоступно для данной конфигурации'],
    ['Carheight', 'CH', 'Carheight.CEIL == $CEIL && Carheight.CH == CH',
      ['$CEILTYPE', 'CH'], 'Высота кабины недоступна для выбранного потолка'],
    ['Doorheight', 'CH', 'Doorheight.CEIL == $CEIL && Doorheight.CH == CH && Doorheight.OPH == OPH',
      ['$CEILTYPE', 'CH', 'OPH'], 'Высота дверного проема недоступна для высоты кабины']
  ];
  if (isR) lookupRules.push(['Counterweight', 'CWTDBG',
    'Counterweight.DL == DL && Counterweight.NBENT == NBENT && Counterweight.CWTLOC == $CWTLOC && Counterweight.CARTYPE == $CARTYPE',
    ['$CARTYPE_MENU', '$CWTLOC_MENU', '$HAND', 'NBENT_MENU'], 'Расположение противовеса недоступно для выбранной кабины']);
  for (const [table, resultColumn, predicate, fields, message] of lookupRules) {
    if (!databases.has(table)) throw new Error(`${source.id}: missing ${table} database`);
    validationRules.push({ name: `xizi_${table.toLowerCase()}`, expression: `FIND(${table}.${resultColumn}, ${predicate}) > 0`,
      message, fieldNames: fields.filter(field => availableNames.has(field)) });
  }

  const roots = [...validationRules.flatMap(rule => dependencyNames(rule.expression, variables)),
    ...parameters.flatMap(parameter => dependencyNames(parameter.levelExpression, variables))];
  const needed = new Set(reference.calculatedVariables.map(item => item.name).filter(name => variables.has(name)));
  const queue = [...roots, ...needed];
  const visited = new Set();
  while (queue.length) {
    const name = queue.pop();
    const variable = variables.get(name);
    if (!variable || variable.external || visited.has(name)) continue;
    visited.add(name);
    needed.add(name);
    queue.push(...dependencyNames(variable.expression, variables));
  }
  const calculatedVariables = inspection.variables
    .filter(variable => needed.has(variable.name) && !variable.external && variable.expression)
    .map(variable => definition(variable, true));

  return {
    id: source.id,
    code: source.id,
    name: isR ? 'UN-Victor R' : reference.name,
    description: `T-FLEX UN-Victor, редакция 21.09.2026; исходные фрагменты сохранены отдельно`,
    templateFilePath: `templates/${source.folder}/${source.grb}`,
    outputFormats: ['pdf', 'dwg', 'dxf'],
    parameters,
    calculatedVariables,
    validationRules,
    lookupTables
  };
}

for (const source of sources) {
  const inspection = JSON.parse((await readFile(source.file, 'utf8')).replace(/^\uFEFF/u, ''));
  const template = createTemplate(source, inspection);
  const index = catalog.templates.findIndex(item => item.id === source.id);
  if (index >= 0) catalog.templates[index] = template;
  else catalog.templates.splice(catalog.templates.findIndex(item => item.id === 'un_victor_mrl_t') + 1, 0, template);
  process.stdout.write(`${source.id}: ${template.parameters.length} inputs, ${template.calculatedVariables.length} calculated, ${template.validationRules.length} rules\n`);
}

await writeFile(catalogPath, `${JSON.stringify(catalog, null, 2).replaceAll('\n', '\r\n')}\r\n`, 'utf8');
