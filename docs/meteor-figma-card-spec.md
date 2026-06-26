# METEOR Figma Card Spec

Спецификация для ручной сборки карточки в Figma без MCP.

## Canvas

- Frame: `640 x 480`
- Background: `#F4F7FB`

## Card

- Position: center
- Size: `296 x 280`
- Radius: `14`
- Fill: `#FFFFFF`
- Stroke: `#D7DEE9`, `1px`
- Shadow: `0 16 40 rgba(15, 39, 78, 0.10)`

## Hero Area

- Position inside card: `12, 12`
- Size: `272 x 126`
- Radius: `12`
- Fill: `#F8FAFD`
- Stroke: `#E6ECF5`, `1px`

### Hero Preview

Draw a simplified engineering preview:

- Grid lines: `#E9EEF6`, `1px`, every `24px`
- Shaft outline: `#102B5C`, `2px`
- Cabin outline: `#102B5C`, `2px`
- Door accent line: `#E84142`, `2px`
- Small badge top-left: `32 x 32`
- Badge radius: `8`
- Badge fill: `#EAF1FF`
- Badge icon/mark: `#102B5C`

### Hero Headline

- Position: around `x: 132`, `y: 38`
- Font: `Montserrat`
- Size: `20`
- Line height: `24`
- Weight: `600`
- Color: `#111827`

Text:

```text
Инженерный
центр чертежей.
```

Highlight the word `центр`:

- Fill: `#EAF1FF`
- Radius: `4`
- Padding: `2px 4px`

## Content

- Start position: `x: 24`, `y: 154`

### Title

- Text: `Создание чертежей`
- Font: `Montserrat`
- Weight: `600`
- Size: `16`
- Line height: `20`
- Color: `#111827`

### Badge

Place next to the title.

- Text: `TFlex шаблоны`
- Font: `Montserrat`
- Weight: `500`
- Size: `10`
- Color: `#6B7280`
- Fill: `#F1F5F9`
- Radius: `4`
- Padding: `4px 6px`

### Description

- Gap from title row: `8px`
- Font: `Montserrat`
- Weight: `400`
- Size: `12`
- Line height: `18`
- Color: `#6B7280`

Text:

```text
Автоматическое создание чертежей
по параметрам без нагрузки на проектный отдел.
```

## Footer

### Divider

- Position: `y: 218`
- Stroke: `#E6ECF5`, `1px`

### Button

- Position: `24, 236`
- Size: `118 x 36`
- Radius: `8`
- Fill: `#0B2E63`

Text:

- Label: `Использовать`
- Font: `Montserrat`
- Weight: `600`
- Size: `12`
- Color: `#FFFFFF`

### Counter

- Position: right side, vertically centered with the button
- Icon: upload/cloud or template icon
- Icon color: `#0B2E63`
- Text: `6 активных шаблонов`
- Font: `Montserrat`
- Weight: `500`
- Size: `12`
- Color: `#6B7280`

## Visual Direction

Карточка должна выглядеть не как маркетплейс-плагин, а как компактная продуктовая карточка модуля METEOR Engineering Center:

- flat/material style
- restrained engineering feel
- navy and white as the main palette
- red only as a technical accent in the preview
- no decorative gradients or oversized marketing elements
