import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToString } from 'react-dom/server'

let vite, ExpensesCtx, AppCtx, GroupsCtx, UserCtx, CatBadge, CategoryLoadError, ExpenseForm
const categories = [
  { id: 'food', name: 'Żywność', description: 'Produkty spożywcze', icon: 'basket', isSystemGenerated: false },
  { id: 'system', name: 'Nieznane', description: '', icon: 'help', isSystemGenerated: true },
]

before(async () => {
  vite = await createServer({ logLevel: 'error' })
  ;({ ExpensesCtx } = await vite.ssrLoadModule('/src/expensesContext.ts'))
  ;({ AppCtx } = await vite.ssrLoadModule('/src/context.ts'))
  ;({ GroupsCtx } = await vite.ssrLoadModule('/src/groupsContext.ts'))
  ;({ UserCtx } = await vite.ssrLoadModule('/src/userContext.ts'))
  ;({ CatBadge, CategoryLoadError } = await vite.ssrLoadModule('/src/views/expenses/ExpenseUi.tsx'))
  ;({ default: ExpenseForm } = await vite.ssrLoadModule('/src/views/expenses/ExpenseForm.tsx'))
})
after(async () => { await vite?.close() })

const render = (child, overrides = {}) => renderToString(createElement(AppCtx.Provider, { value: { accounts: [] } },
  createElement(UserCtx.Provider, { value: { profile: { id: 'payer' } } },
    createElement(GroupsCtx.Provider, { value: { rev: 0 } },
      createElement(ExpensesCtx.Provider, { value: { categories, categoriesLoading: false, categoriesError: null, ...overrides } }, child)))))

test('category badges use the current React catalog without a separate registry', () => {
  const badge = createElement(CatBadge, { id: 'food' })
  assert.match(render(badge), /Żywność/)
  assert.match(render(badge, { categories: [{ ...categories[0], name: 'Zaktualizowana nazwa' }] }), /Zaktualizowana nazwa/)
})

test('category badges resolve icon names from the API and use the shared theme colors', () => {
  const examples = [
    ['shopping-cart', 'bg-mint-soft', false],
    ['badge-percent', 'bg-mint-soft', false],
    ['recycle', 'bg-mint-soft', false],
    ['circle-help', 'bg-wash', true],
  ]
  for (const [icon, color, isSystemGenerated] of examples) {
    const badge = createElement(CatBadge, { id: 'food' })
    const html = render(badge, { categories: [{ ...categories[0], icon, isSystemGenerated }] })
    assert.match(html, new RegExp(`lucide-${icon}`))
    assert.match(html, new RegExp(color))
  }
})

test('the form populates category choices from the same catalog and excludes the system category', () => {
  const html = render(createElement(ExpenseForm, { initial: { form: {
    groupId: 'group', payerId: 'payer', shares: [{ userId: 'payer', percentage: '100' }],
    items: [{ id: 'item', name: 'Chleb', categoryId: 'food', unit: 'szt.', unitPrice: '5', quantity: '1', discount: '' }],
  } } }))
  assert.match(html, /<option value="food" selected="">Żywność<\/option>/)
  assert.doesNotMatch(html, /<option value="system"/)
})

test('invalid discounts immediately show an error and block preview before submitting the form', () => {
  for (const discount of ['1', '-10.01']) {
    const html = render(createElement(ExpenseForm, { initial: { form: {
      groupId: 'group', payerId: 'payer', shares: [{ userId: 'payer', percentage: '100' }], amount: '20',
      items: [{ id: 'item', name: 'Produkt', categoryId: '', unit: '', unitPrice: '5', quantity: '2', discount }],
    } } }))
    assert.match(html, discount.startsWith('-') ? /Wartość produktu po rabacie nie może być ujemna/ : /Rabat nie może być dodatni/)
    assert.match(html, /<button[^>]*disabled=""[^>]*>Podgląd wydatku<\/button>/)
    assert.match(html, /placeholder="0" aria-invalid="true"/)
  }
})

test('invalid products show a dash and are excluded from the displayed product sum', () => {
  const item = { id: 'invalid', name: 'Produkt', categoryId: 'food', unit: '', unitPrice: '100', quantity: '1', discount: '' }
  for (const change of [{ name: '' }, { categoryId: '' }, { unitPrice: '' }, { quantity: '0' }, { discount: '1' }, { discount: '-101' }]) {
    const html = render(createElement(ExpenseForm, { initial: { form: {
      amount: '200', items: [
        { ...item, id: 'valid', unitPrice: '5' },
        { ...item, ...change },
      ],
    } } }))
    assert.match(html, /RAZEM:<\/span> <span[^>]*>-<\/span>/)
    assert.match(html, /Suma produktów<\/span><span[^>]*>5[,.]00\s*(?:&nbsp;|\u00a0| )?zł<\/span>/)
  }
})

test('loading and failed category requests are distinct from a system-generated unknown category', () => {
  const badge = createElement(CatBadge, { id: 'food' })
  assert.match(render(badge, { categories: [], categoriesLoading: true }), /Ładowanie kategorii…/)
  assert.match(render(badge, { categories: [], categoriesError: 503 }), /Kategoria niedostępna/)
  assert.match(render(createElement(CatBadge, { id: 'system' })), /Nieznane/)
  const error = render(createElement(CategoryLoadError), { categoriesError: 503 })
  assert.match(error, /Nie udało się pobrać kategorii/)
  assert.match(error, /Spróbuj ponownie/)
})
