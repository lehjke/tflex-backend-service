const MAX_EXPRESSION_LENGTH = 8192;
const MAX_TOKENS = 4096;
const MAX_DEPTH = 64;
const MAX_EVALUATED_STRING_LENGTH = 64 * 1024;
const MAX_STRING_ALLOCATION_WORK = 256 * 1024;
const MAX_LOOKUP_ROWS = 100_000;

const UNKNOWN = Object.freeze({ known: false, value: undefined });
const AMBIGUOUS = Object.freeze({ known: false, value: undefined, ambiguous: true });

function known(value) {
  return { known: true, value };
}

class ExpressionError extends Error {
}

function truncateTopLevelSemicolon(expression) {
  let depth = 0;
  let quote = "";

  for (let index = 0; index < expression.length; index += 1) {
    const current = expression[index];
    const previous = expression[index - 1];
    if (quote) {
      if (current === quote && previous !== "\\") quote = "";
      continue;
    }

    if (current === "\"" || current === "'") {
      quote = current;
    } else if (current === "(") {
      depth += 1;
    } else if (current === ")") {
      depth = Math.max(0, depth - 1);
    } else if (current === ";" && depth === 0) {
      return expression.slice(0, index).trim();
    }
  }

  return expression;
}

function isLetter(value) {
  return typeof value === "string" && value.length === 1 && /\p{L}/u.test(value);
}

function isDigit(value) {
  return typeof value === "string" && value.length === 1 && value >= "0" && value <= "9";
}

function isIdentifierPart(value) {
  return value === "_" || value === "$" || isLetter(value) || isDigit(value);
}

class Lexer {
  constructor(expression) {
    this.expression = expression;
    this.position = 0;
  }

  nextToken() {
    while (this.position < this.expression.length && /\s/u.test(this.expression[this.position])) {
      this.position += 1;
    }

    if (this.position >= this.expression.length) return { kind: "end" };

    const current = this.expression[this.position];
    if (isDigit(current) || (current === "." && isDigit(this.expression[this.position + 1]))) {
      return this.readNumber();
    }

    if (current === "\"" || current === "'") return this.readString();
    if (current === "$" || current === "_" || isLetter(current)) return this.readIdentifier();

    this.position += 1;
    const pair = current + (this.expression[this.position] || "");
    const pairedKinds = {
      "**": "doubleStar",
      "!=": "bangEqual",
      "&&": "andAnd",
      "||": "orOr",
      "==": "equalEqual",
      "<=": "lessEqual",
      ">=": "greaterEqual"
    };
    if (pairedKinds[pair]) {
      this.position += 1;
      return { kind: pairedKinds[pair] };
    }

    const singleKinds = {
      "+": "plus",
      "-": "minus",
      "*": "star",
      "/": "slash",
      "%": "percent",
      "^": "caret",
      "!": "bang",
      "<": "less",
      ">": "greater",
      "?": "question",
      ":": "colon",
      "(": "leftParenthesis",
      ")": "rightParenthesis",
      ",": "comma",
      ".": "dot"
    };
    return { kind: singleKinds[current] || "invalid" };
  }

  readNumber() {
    const start = this.position;
    while (isDigit(this.expression[this.position])) this.position += 1;

    if (this.expression[this.position] === ".") {
      this.position += 1;
      while (isDigit(this.expression[this.position])) this.position += 1;
    }

    if (this.expression[this.position] === "e" || this.expression[this.position] === "E") {
      this.position += 1;
      if (this.expression[this.position] === "+" || this.expression[this.position] === "-") {
        this.position += 1;
      }

      const exponentStart = this.position;
      while (isDigit(this.expression[this.position])) this.position += 1;
      if (exponentStart === this.position) return { kind: "invalid" };
    }

    const value = Number(this.expression.slice(start, this.position));
    return Number.isFinite(value) ? { kind: "number", value } : { kind: "invalid" };
  }

  readString() {
    const quote = this.expression[this.position];
    this.position += 1;
    let value = "";
    let interpolationDepth = 0;
    let interpolationQuote = "";

    while (this.position < this.expression.length) {
      const current = this.expression[this.position];
      this.position += 1;
      const previous = this.expression[this.position - 2];

      if (interpolationDepth > 0) {
        value += current;
        if (interpolationQuote) {
          if (current === interpolationQuote && previous !== "\\") interpolationQuote = "";
        } else if (current === "\"" || current === "'") {
          interpolationQuote = current;
        } else if (current === "{") {
          interpolationDepth += 1;
        } else if (current === "}") {
          interpolationDepth -= 1;
        }
        continue;
      }

      if (current === quote) return { kind: "string", value, interpolated: value.includes("{") };
      if (current === "{") {
        interpolationDepth = 1;
        value += current;
        continue;
      }

      if (current !== "\\" || this.position >= this.expression.length) {
        value += current;
        continue;
      }

      const escaped = this.expression[this.position];
      this.position += 1;
      value += escaped === "n"
        ? "\n"
        : escaped === "r"
          ? "\r"
          : escaped === "t"
            ? "\t"
            : escaped;
    }

    return { kind: "invalid" };
  }

  readIdentifier() {
    const start = this.position;
    this.position += 1;
    while (isIdentifierPart(this.expression[this.position])) this.position += 1;
    return {
      kind: "identifier",
      value: this.expression.slice(start, this.position)
    };
  }
}

class Parser {
  constructor(expression) {
    this.lexer = new Lexer(expression);
    this.current = null;
    this.tokenCount = 0;
    this.depth = 0;
    this.advance();
  }

  parse() {
    const result = this.parseConditional();
    this.expect("end");
    return result;
  }

  parseConditional() {
    const condition = this.parseOr();
    if (!this.match("question")) return condition;

    return this.nested(() => {
      if (this.match("leftParenthesis")) {
        const whenTrue = this.parseConditional();
        if (this.match("colon")) {
          const whenFalse = this.parseConditional();
          this.expect("rightParenthesis");
          return this.parsePostConditionalContinuation({
            type: "conditional",
            condition,
            whenTrue,
            whenFalse
          });
        }

        this.expect("rightParenthesis");
        this.expect("colon");
        return {
          type: "conditional",
          condition,
          whenTrue,
          whenFalse: this.parseConditional()
        };
      }

      const whenTrue = this.parseConditional();
      this.expect("colon");
      const whenFalse = this.parseConditional();
      return { type: "conditional", condition, whenTrue, whenFalse };
    });
  }

  parsePostConditionalContinuation(value) {
    let left = value;
    if (this.match("caret")) {
      left = { type: "binary", operation: "caret", left, right: this.parsePower() };
    }
    while (["star", "doubleStar", "slash", "percent"].includes(this.current.kind)) {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parsePower() };
    }
    while (this.current.kind === "plus" || this.current.kind === "minus") {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parseMultiplicative() };
    }
    while (["less", "lessEqual", "greater", "greaterEqual"].includes(this.current.kind)) {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parseAdditive() };
    }
    while (this.current.kind === "equalEqual" || this.current.kind === "bangEqual") {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parseRelational() };
    }
    while (this.match("andAnd")) {
      left = { type: "binary", operation: "andAnd", left, right: this.parseEquality() };
    }
    while (this.match("orOr")) {
      left = { type: "binary", operation: "orOr", left, right: this.parseAnd() };
    }
    return left;
  }

  parseOr() {
    let left = this.parseAnd();
    while (this.match("orOr")) {
      left = { type: "binary", operation: "orOr", left, right: this.parseAnd() };
    }

    return left;
  }

  parseAnd() {
    let left = this.parseEquality();
    while (this.match("andAnd")) {
      left = { type: "binary", operation: "andAnd", left, right: this.parseEquality() };
    }

    return left;
  }

  parseEquality() {
    let left = this.parseRelational();
    while (this.current.kind === "equalEqual" || this.current.kind === "bangEqual") {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parseRelational() };
    }

    return left;
  }

  parseRelational() {
    let left = this.parseAdditive();
    while (["less", "lessEqual", "greater", "greaterEqual"].includes(this.current.kind)) {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parseAdditive() };
    }

    return left;
  }

  parseAdditive() {
    let left = this.parseMultiplicative();
    while (this.current.kind === "plus" || this.current.kind === "minus") {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parseMultiplicative() };
    }

    return left;
  }

  parseMultiplicative() {
    let left = this.parsePower();
    while (["star", "doubleStar", "slash", "percent"].includes(this.current.kind)) {
      const operation = this.current.kind;
      this.advance();
      left = { type: "binary", operation, left, right: this.parsePower() };
    }

    return left;
  }

  parsePower() {
    const left = this.parseUnary();
    return this.match("caret")
      ? { type: "binary", operation: "caret", left, right: this.parsePower() }
      : left;
  }

  parseUnary() {
    if (!["bang", "plus", "minus"].includes(this.current.kind)) return this.parsePrimary();
    const operation = this.current.kind;
    this.advance();
    return this.nested(() => ({ type: "unary", operation, operand: this.parseUnary() }));
  }

  parsePrimary() {
    if (this.current.kind === "number" || this.current.kind === "string") {
      const node = {
        type: "literal",
        value: this.current.value,
        interpolated: Boolean(this.current.interpolated)
      };
      this.advance();
      return node;
    }

    if (this.current.kind === "identifier") {
      const name = this.current.value;
      this.advance();
      if (this.match("leftParenthesis")) return this.parseFunction(name);

      const path = [name];
      while (this.match("dot")) {
        if (this.current.kind !== "identifier") throw new ExpressionError();
        path.push(this.current.value);
        this.advance();
      }

      return { type: "identifier", path };
    }

    if (this.match("leftParenthesis")) {
      return this.nested(() => {
        const value = this.parseConditional();
        this.expect("rightParenthesis");
        return value;
      });
    }

    throw new ExpressionError();
  }

  parseFunction(name) {
    return this.nested(() => {
      const argumentsList = [];
      if (!this.match("rightParenthesis")) {
        do {
          argumentsList.push(this.parseConditional());
        } while (this.match("comma"));
        this.expect("rightParenthesis");
      }

      return { type: "function", name, arguments: argumentsList };
    });
  }

  match(kind) {
    if (this.current.kind !== kind) return false;
    this.advance();
    return true;
  }

  expect(kind) {
    if (!this.match(kind)) throw new ExpressionError();
  }

  advance() {
    this.tokenCount += 1;
    if (this.tokenCount > MAX_TOKENS) throw new ExpressionError();
    this.current = this.lexer.nextToken();
    if (this.current.kind === "invalid") throw new ExpressionError();
  }

  nested(callback) {
    this.depth += 1;
    if (this.depth > MAX_DEPTH) throw new ExpressionError();
    try {
      return callback();
    } finally {
      this.depth -= 1;
    }
  }
}

const FUNCTION_ARITY = {
  error: [0, Number.POSITIVE_INFINITY],
  warn: [0, Number.POSITIVE_INFINITY],
  select: [1, Number.POSITIVE_INFINITY],
  switch: [2, Number.POSITIVE_INFINITY],
  atof: [1, 1],
  ftoa: [1, 1],
  ftoa2: [1, 1],
  val: [1, 2],
  tpart: [2, 3],
  ltot: [1, 4],
  abs: [1, 1],
  floor: [1, 1],
  sin: [1, 1],
  cos: [1, 1],
  tan: [1, 1],
  ceil: [1, 2],
  round: [1, 2],
  min: [1, Number.POSITIVE_INFINITY],
  max: [1, Number.POSITIVE_INFINITY],
  get: [0, Number.POSITIVE_INFINITY],
  getv: [0, Number.POSITIVE_INFINITY],
  tgetv: [1, 1],
  find: [2, 2],
  ch_level: [2, 2],
  ch_front: [2, 2],
  ch_rear: [2, 2],
  ch_lvl: [3, 3],
  ch_hf: [3, 3],
  ch_maxhf: [3, 3],
  ch_thf: [2, 2],
  ch_maxthf: [2, 2]
};

function splitInterpolatedString(value) {
  const segments = [];
  let literalStart = 0;
  let index = 0;

  while (index < value.length) {
    if (value[index] !== "{") {
      index += 1;
      continue;
    }

    if (literalStart < index) {
      segments.push({ type: "literal", value: value.slice(literalStart, index) });
    }

    const expressionStart = index + 1;
    let depth = 1;
    let quote = "";
    index += 1;
    for (; index < value.length; index += 1) {
      const current = value[index];
      const previous = value[index - 1];
      if (quote) {
        if (current === quote && previous !== "\\") quote = "";
      } else if (current === "\"" || current === "'") {
        quote = current;
      } else if (current === "{") {
        depth += 1;
      } else if (current === "}") {
        depth -= 1;
        if (depth === 0) break;
      }
    }

    if (depth !== 0) throw new ExpressionError();
    segments.push({
      type: "expression",
      value: parseExpression(value.slice(expressionStart, index))
    });
    index += 1;
    literalStart = index;
  }

  if (literalStart < value.length) {
    segments.push({ type: "literal", value: value.slice(literalStart) });
  }
  return segments;
}

function validateAst(node) {
  if (node.type === "literal") {
    return !node.interpolated
      || splitInterpolatedString(node.value)
        .every(segment => segment.type === "literal" || validateAst(segment.value));
  }
  if (node.type === "identifier") return true;
  if (node.type === "unary") return validateAst(node.operand);
  if (node.type === "binary") return validateAst(node.left) && validateAst(node.right);
  if (node.type === "conditional") {
    return validateAst(node.condition) && validateAst(node.whenTrue) && validateAst(node.whenFalse);
  }

  if (node.type !== "function") return false;
  const arity = FUNCTION_ARITY[node.name.toLowerCase()];
  return Boolean(arity)
    && node.arguments.length >= arity[0]
    && node.arguments.length <= arity[1]
    && node.arguments.every(validateAst);
}

function parseExpression(expression) {
  if (typeof expression !== "string"
    || !expression.trim()
    || expression.length > MAX_EXPRESSION_LENGTH) {
    throw new ExpressionError();
  }

  return new Parser(truncateTopLevelSemicolon(expression)).parse();
}

function readOwnCaseInsensitive(source, name) {
  if (!source || typeof source !== "object") return UNKNOWN;
  if (Object.prototype.hasOwnProperty.call(source, name) && source[name] !== null && source[name] !== undefined) {
    return known(source[name]);
  }

  const normalized = name.toLocaleLowerCase("en-US");
  const keys = Object.keys(source).filter(candidate =>
    candidate.toLocaleLowerCase("en-US") === normalized
      && source[candidate] !== null
      && source[candidate] !== undefined);
  if (keys.length === 1) return known(source[keys[0]]);
  return keys.length > 1 ? AMBIGUOUS : UNKNOWN;
}

function readVariable(environment, name) {
  const direct = readOwnCaseInsensitive(environment.rowValues, name);
  if (direct.known || direct.ambiguous) return direct;

  const value = readOwnCaseInsensitive(environment.context, name);
  if (value.known || value.ambiguous) return value;

  const alias = name.startsWith("$") ? name.slice(1) : `$${name}`;
  return readOwnCaseInsensitive(environment.context, alias);
}

function readIdentifier(environment, path) {
  if (path.length === 1) {
    const normalized = path[0].toLowerCase();
    if (normalized === "true") return known(true);
    if (normalized === "false") return known(false);
    return readVariable(environment, path[0]);
  }

  if (environment.rowTable
    && path[0].toLocaleLowerCase("en-US") === environment.rowTable.toLocaleLowerCase("en-US")) {
    let value = known(environment.rowValues);
    for (const segment of path.slice(1)) {
      value = value.known ? readOwnCaseInsensitive(value.value, segment) : UNKNOWN;
    }
    return value;
  }

  let value = readVariable(environment, path[0]);
  for (const segment of path.slice(1)) {
    value = value.known ? readOwnCaseInsensitive(value.value, segment) : UNKNOWN;
  }
  return value;
}

function toNumber(value) {
  if (!value.known) return null;
  if (typeof value.value === "boolean") return value.value ? 1 : 0;
  if (typeof value.value === "number") return Number.isFinite(value.value) ? value.value : null;
  if (typeof value.value === "string" && value.value.trim()) {
    const number = Number(value.value);
    return Number.isFinite(number) ? number : null;
  }
  return null;
}

function toCondition(value) {
  if (!value.known) return null;
  if (typeof value.value === "boolean") return value.value;
  const number = toNumber(value);
  if (number !== null) return number !== 0;
  if (typeof value.value === "string") return value.value.length > 0;
  return null;
}

function invariantText(value) {
  if (!value.known || value.value === null || value.value === undefined) return "";
  return String(value.value);
}

function applyEquality(left, right) {
  if (!left.known || !right.known) return UNKNOWN;
  const leftNumber = toNumber(left);
  const rightNumber = toNumber(right);
  if (leftNumber !== null && rightNumber !== null) return known(leftNumber === rightNumber);
  if (typeof left.value === "boolean" && typeof right.value === "boolean") {
    return known(left.value === right.value);
  }
  if (typeof left.value === "string" && typeof right.value === "string") {
    return known(left.value === right.value);
  }
  return known(false);
}

function applyBinary(operation, left, right, environment) {
  if (operation === "andAnd" || operation === "orOr") {
    const leftCondition = toCondition(left);
    const rightCondition = toCondition(right);
    if (operation === "andAnd") {
      if (leftCondition === false || rightCondition === false) return known(false);
      return leftCondition !== null && rightCondition !== null ? known(true) : UNKNOWN;
    }

    if (leftCondition === true || rightCondition === true) return known(true);
    return leftCondition !== null && rightCondition !== null ? known(false) : UNKNOWN;
  }

  if (operation === "equalEqual" || operation === "bangEqual") {
    const equal = applyEquality(left, right);
    return operation === "bangEqual" && equal.known ? known(!equal.value) : equal;
  }

  if (["less", "lessEqual", "greater", "greaterEqual"].includes(operation)) {
    if (!left.known || !right.known) return UNKNOWN;
    const leftNumber = toNumber(left);
    const rightNumber = toNumber(right);
    let comparison = null;
    if (leftNumber !== null && rightNumber !== null) {
      comparison = leftNumber === rightNumber ? 0 : leftNumber < rightNumber ? -1 : 1;
    } else if (typeof left.value === "string" && typeof right.value === "string") {
      comparison = left.value === right.value ? 0 : left.value < right.value ? -1 : 1;
    }
    if (comparison === null) return UNKNOWN;
    return known(operation === "less"
      ? comparison < 0
      : operation === "lessEqual"
        ? comparison <= 0
        : operation === "greater"
          ? comparison > 0
          : comparison >= 0);
  }

  if (operation === "plus"
    && left.known
    && right.known
    && (typeof left.value === "string" || typeof right.value === "string")) {
    const leftText = invariantText(left);
    const rightText = invariantText(right);
    const length = leftText.length + rightText.length;
    if (length > MAX_EVALUATED_STRING_LENGTH
      || environment.stringAllocationWork + length > MAX_STRING_ALLOCATION_WORK) {
      throw new ExpressionError();
    }
    environment.stringAllocationWork += length;
    return known(leftText + rightText);
  }

  const leftNumber = toNumber(left);
  const rightNumber = toNumber(right);
  if (leftNumber === null || rightNumber === null) return UNKNOWN;

  const result = operation === "plus"
    ? leftNumber + rightNumber
    : operation === "minus"
      ? leftNumber - rightNumber
      : operation === "star" || operation === "doubleStar"
        ? leftNumber * rightNumber
        : operation === "slash"
          ? rightNumber === 0 ? Number.NaN : leftNumber / rightNumber
          : operation === "percent"
            ? rightNumber === 0 ? Number.NaN : leftNumber % rightNumber
            : operation === "caret"
              ? leftNumber ** rightNumber
              : Number.NaN;
  return Number.isFinite(result) ? known(result) : UNKNOWN;
}

function roundAwayFromZero(value) {
  return Math.sign(value) * Math.floor(Math.abs(value) + 0.5);
}

function evaluateSelect(argumentsList) {
  let unknownSeen = false;
  for (let index = 0; index + 1 < argumentsList.length; index += 2) {
    const condition = toCondition(argumentsList[index]);
    if (condition === null) {
      unknownSeen = true;
    } else if (condition) {
      return unknownSeen ? UNKNOWN : argumentsList[index + 1];
    }
  }
  if (unknownSeen) return UNKNOWN;
  return argumentsList.length % 2 === 1 ? argumentsList.at(-1) : UNKNOWN;
}

function evaluateSwitch(argumentsList) {
  if (argumentsList.length < 2 || !argumentsList[0].known) return UNKNOWN;
  for (let index = 1; index + 1 < argumentsList.length; index += 2) {
    const equal = applyEquality(argumentsList[0], argumentsList[index]);
    if (!equal.known) return UNKNOWN;
    if (equal.value) return argumentsList[index + 1];
  }
  return argumentsList.length % 2 === 0 ? argumentsList.at(-1) : UNKNOWN;
}

function getContextNumber(environment, name) {
  return toNumber(readVariable(environment, name));
}

function evaluateTemplateHelper(name, argumentsList, environment) {
  const values = argumentsList.map(toNumber);
  const stops = getContextNumber(environment, "stops");

  if (name === "ch_level" && values.every(value => value !== null) && stops !== null) {
    const index = Math.trunc(values[1]);
    if (index < stops) return known(values[0]);
    if (index === stops) {
      const topLevel = getContextNumber(environment, "s_top_level_1");
      return topLevel === null ? UNKNOWN : known(topLevel);
    }
    return known(0);
  }

  if ((name === "ch_front" || name === "ch_rear")
    && values.every(value => value !== null)
    && stops !== null) {
    const index = Math.trunc(values[0]);
    if (index < stops) return known(values[1] === 0 ? 0 : 1);
    if (index === stops) {
      if (name === "ch_front") return known(1);
      const topRear = getContextNumber(environment, "s_top_rear");
      return topRear === null ? UNKNOWN : known(topRear === 0 ? 0 : 1);
    }
    return known(0);
  }

  if (name === "ch_lvl" && values.every(value => value !== null) && stops !== null) {
    return known(Math.trunc(values[2]) <= stops && values[0] === 0 && values[1] === 0 ? 0 : 1);
  }

  if ((name === "ch_hf" || name === "ch_maxhf")
    && values.every(value => value !== null)
    && stops !== null) {
    if (Math.trunc(values[2]) > stops) return known(1);
    const distance = values[1] - values[0];
    if (name === "ch_maxhf") return known(distance <= 11000 ? 1 : 0);
    const minimumHeight = getContextNumber(environment, "S_HF");
    return minimumHeight === null ? UNKNOWN : known(distance >= minimumHeight ? 1 : 0);
  }

  if ((name === "ch_thf" || name === "ch_maxthf")
    && values.every(value => value !== null)
    && stops !== null) {
    const finalLevel = getContextNumber(environment, "s_top_level_1");
    if (finalLevel === null) return UNKNOWN;
    if (Math.trunc(values[1]) !== stops - 1) return known(1);
    const distance = finalLevel - values[0];
    if (name === "ch_maxthf") return known(distance <= 11000 ? 1 : 0);
    const minimumHeight = getContextNumber(environment, "S_HF");
    return minimumHeight === null ? UNKNOWN : known(distance >= minimumHeight ? 1 : 0);
  }

  return UNKNOWN;
}

function evaluateFind(node, environment) {
  if (node.arguments.length !== 2
    || node.arguments[0].type !== "identifier"
    || node.arguments[0].path.length !== 2) {
    return UNKNOWN;
  }

  const [tableName, fieldName] = node.arguments[0].path;
  const table = readOwnCaseInsensitive(environment.lookupTables, tableName);
  if (!table.known || !Array.isArray(table.value)) return UNKNOWN;

  for (const row of table.value) {
    environment.lookupRowWork += 1;
    if (environment.lookupRowWork > MAX_LOOKUP_ROWS) throw new ExpressionError();
    const predicateEnvironment = {
      ...environment,
      rowTable: tableName,
      rowValues: row
    };
    const predicate = evaluateNode(node.arguments[1], predicateEnvironment);
    environment.stringAllocationWork = predicateEnvironment.stringAllocationWork;
    if (toCondition(predicate) === true) {
      return readOwnCaseInsensitive(row, fieldName);
    }
  }

  return known(0);
}

function evaluateFunction(node, environment) {
  const name = node.name.toLowerCase();
  if (name === "error") return known(0);
  if (name === "warn") return known(1);
  if (name === "find") return evaluateFind(node, environment);

  const argumentsList = node.arguments.map(argument => evaluateNode(argument, environment));
  if (name === "select") return evaluateSelect(argumentsList);
  if (name === "switch") return evaluateSwitch(argumentsList);
  if (name === "get" || name === "getv") return known(0);

  if (name === "tgetv" && argumentsList.length === 1 && argumentsList[0].known) {
    const now = environment.now;
    const parts = {
      year: now.getFullYear(),
      month: now.getMonth() + 1,
      day: now.getDate(),
      hour: now.getHours(),
      minute: now.getMinutes(),
      second: now.getSeconds()
    };
    return known(String(parts[invariantText(argumentsList[0]).trim().toLowerCase()] ?? ""));
  }

  if ((name === "atof" || name === "val") && argumentsList.length === 1) {
    const number = toNumber(argumentsList[0]);
    return number === null ? argumentsList[0].known ? known(0) : UNKNOWN : known(number);
  }

  if (name === "val" && argumentsList.length === 2) {
    const lookupValue = toNumber(argumentsList[1]);
    const fallback = toNumber(argumentsList[0]);
    return lookupValue !== null ? known(lookupValue) : fallback !== null ? known(fallback) : UNKNOWN;
  }

  if ((name === "ftoa" || name === "ftoa2") && argumentsList.length === 1 && argumentsList[0].known) {
    return known(invariantText(argumentsList[0]));
  }

  if (name === "tpart"
    && (argumentsList.length === 2 || argumentsList.length === 3)
    && argumentsList[0].known) {
    const start = toNumber(argumentsList[1]);
    const length = argumentsList.length === 3 ? toNumber(argumentsList[2]) : 0;
    if (start === null || length === null) return UNKNOWN;
    const text = invariantText(argumentsList[0]);
    const offset = Math.max(0, Math.trunc(start) - 1);
    const count = Math.trunc(length);
    const selected = (count > 0 ? text.slice(offset, offset + count) : text.slice(offset)).trim();
    return selected.length <= MAX_EVALUATED_STRING_LENGTH ? known(selected) : UNKNOWN;
  }

  if (name === "ltot" && argumentsList.length >= 1 && argumentsList.length <= 4) {
    const number = toNumber(argumentsList[0]);
    if (number === null) return UNKNOWN;
    const requestedPrecision = argumentsList.length >= 4 ? toNumber(argumentsList[3]) : 3;
    const precision = Math.min(20, Math.max(0, Math.trunc(requestedPrecision ?? 3)));
    return known(number.toFixed(precision));
  }

  if ((name === "min" || name === "max") && argumentsList.length > 0) {
    const numbers = argumentsList.map(toNumber);
    return numbers.every(value => value !== null)
      ? known(name === "min" ? Math.min(...numbers) : Math.max(...numbers))
      : UNKNOWN;
  }

  if (argumentsList.length === 1) {
    const number = toNumber(argumentsList[0]);
    if (number !== null) {
      const radians = number * Math.PI / 180;
      const result = name === "abs"
        ? Math.abs(number)
        : name === "floor"
          ? Math.floor(number)
          : name === "ceil"
            ? Math.ceil(number)
            : name === "round"
              ? roundAwayFromZero(number)
              : name === "sin"
                ? Math.sin(radians)
                : name === "cos"
                  ? Math.cos(radians)
                  : name === "tan"
                    ? Math.tan(radians)
                    : Number.NaN;
      if (Number.isFinite(result)) return known(result);
    }
  }

  if ((name === "ceil" || name === "round") && argumentsList.length === 2) {
    const value = toNumber(argumentsList[0]);
    const step = toNumber(argumentsList[1]);
    if (value !== null && step !== null && step !== 0) {
      const increment = Math.abs(step);
      return known((name === "ceil" ? Math.ceil(value / increment) : roundAwayFromZero(value / increment)) * increment);
    }
  }

  return evaluateTemplateHelper(name, argumentsList, environment);
}

function evaluateNode(node, environment) {
  if (node.type === "literal") {
    if (!node.interpolated) return known(node.value);

    let result = "";
    for (const segment of splitInterpolatedString(node.value)) {
      let value = segment.value;
      if (segment.type === "expression") {
        const evaluated = evaluateNode(segment.value, environment);
        if (!evaluated.known) return UNKNOWN;
        value = invariantText(evaluated);
      }
      if (result.length + value.length > MAX_EVALUATED_STRING_LENGTH
        || environment.stringAllocationWork + result.length + value.length > MAX_STRING_ALLOCATION_WORK) {
        throw new ExpressionError();
      }
      environment.stringAllocationWork += result.length + value.length;
      result += value;
    }
    return known(result);
  }
  if (node.type === "identifier") return readIdentifier(environment, node.path);

  if (node.type === "unary") {
    const operand = evaluateNode(node.operand, environment);
    if (node.operation === "bang") {
      const condition = toCondition(operand);
      return condition === null ? UNKNOWN : known(!condition);
    }
    const number = toNumber(operand);
    return number === null ? UNKNOWN : known(node.operation === "minus" ? -number : number);
  }

  if (node.type === "binary") {
    return applyBinary(
      node.operation,
      evaluateNode(node.left, environment),
      evaluateNode(node.right, environment),
      environment);
  }

  if (node.type === "conditional") {
    const condition = toCondition(evaluateNode(node.condition, environment));
    if (condition !== null) {
      return evaluateNode(condition ? node.whenTrue : node.whenFalse, environment);
    }

    const whenTrue = evaluateNode(node.whenTrue, environment);
    const whenFalse = evaluateNode(node.whenFalse, environment);
    const equal = applyEquality(whenTrue, whenFalse);
    return equal.known && equal.value ? whenTrue : UNKNOWN;
  }

  return node.type === "function" ? evaluateFunction(node, environment) : UNKNOWN;
}

export function validateTFlexExpression(expression) {
  try {
    return validateAst(parseExpression(expression));
  } catch {
    return false;
  }
}

export function evaluateTFlexExpression(expression, context, options = {}) {
  try {
    const ast = parseExpression(expression);
    if (!validateAst(ast)) return undefined;

    const environment = {
      context: context && typeof context === "object" ? context : {},
      lookupTables: options.lookupTables && typeof options.lookupTables === "object"
        ? options.lookupTables
        : {},
      now: options.now instanceof Date ? options.now : new Date(),
      rowTable: "",
      rowValues: null,
      stringAllocationWork: 0,
      lookupRowWork: 0
    };
    const result = evaluateNode(ast, environment);
    if (!result.known) return undefined;
    if (typeof result.value === "string" && result.value.length > MAX_EVALUATED_STRING_LENGTH) {
      return undefined;
    }
    return result.value;
  } catch {
    return undefined;
  }
}
