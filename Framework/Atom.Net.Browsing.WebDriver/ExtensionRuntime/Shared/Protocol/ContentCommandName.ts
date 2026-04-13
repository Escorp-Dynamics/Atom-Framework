export const contentCommandNames = [
    'ExecuteScript',
    'FindElement',
    'FindElements',
    'GetElementProperty',
    'ResolveElementScreenPoint',
    'DescribeElement',
    'FocusElement',
    'ScrollElementIntoView',
    'WaitForElement',
    'ApplyContext',
    'CheckShadowRoot',
] as const;

export type ContentCommandName = (typeof contentCommandNames)[number];