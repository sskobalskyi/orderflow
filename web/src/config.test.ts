import { describe, expect, it } from 'vitest'
import { readBranding } from './config'

describe('readBranding', () => {
  it('defaults to Orderflow without a link', () => {
    expect(readBranding({})).toEqual({ shopName: 'Orderflow', homeUrl: null })
  })

  it('uses the configured name and link', () => {
    expect(readBranding({ VITE_SHOP_NAME: ' Greenhaus shop ', VITE_HOME_URL: 'http://localhost:5174' })).toEqual({
      shopName: 'Greenhaus shop',
      homeUrl: 'http://localhost:5174/',
    })
  })

  it.each(['javascript:alert(1)', 'not a url', 'ftp://example.com', ''])('ignores an unusable link %j', (url) => {
    expect(readBranding({ VITE_HOME_URL: url }).homeUrl).toBeNull()
  })
})
