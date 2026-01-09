# LinkPrice 쿠키 분석 리포트

> 분석일: 2026-01-09

## 요약

| 구분 | 수량 |
|------|------|
| 총 분석 사이트 | 22개 |
| 정상 작동 | 20개 |
| 미작동 | 2개 |

---

## 상세 분석 결과

### LinkPrice 정상 작동 사이트

| 사이트 | 제휴 ID | 쿠키값 (앞 10자리) | 쿠키명 | 암호화 방식 | 비고 |
|--------|---------|-------------------|--------|-------------|------|
| gmarket.co.kr | A100637853 | eyJ0b2tlbl... | token | Base64 JSON | inflowChannelToken에 ID 포함 |
| auction.co.kr | A100637853 | A100637853... | PARTNERSHIP_AID | Plain Text | PARTNERSHIP 쿠키 시스템 사용 |
| 11st.co.kr | A100637853 | A100637853... | XSITE_DETAIL | Plain Text (Pipe) | XSITE 시스템 사용 |
| yes24.com | A100637853 | A100637853... | LPINFO | Plain Text (Pipe) | 표준 LPINFO 형식 |
| kyobobook.co.kr | A100637853 | A100637853... | LPINFO | URL Encoded (Pipe) | 표준 LPINFO 형식 |
| interpark.com | A100637853 | A100637853... | LPINFO | URL Encoded (Pipe) | interpark3 suffix 포함 |
| e-himart.co.kr | A100637853 | A100637853... | LPINFO | Plain Text (Pipe) | 표준 LPINFO 형식 |
| soomgo.com | A100637853 | A100637853... | LPINFO / airbridge_utm | URL Encoded + JSON | Airbridge 연동 |
| klook.com | A100637853 | A100637853... | aid_extra | JSON | aff_pid 필드에 ID |
| 99flower.co.kr | A100637853 | A100637853... | LPINFO | URL Encoded (Pipe) | 표준 LPINFO 형식 |
| mootoon.co.kr | A100637853 | A100637853... | LPINFO | URL Encoded (Pipe) | 표준 LPINFO 형식 |
| tstation.com | A100637853 | A100637853... | LPINFO | URL Encoded (Pipe) | 표준 LPINFO 형식 |
| ttang.com | A100637853 | A100637853... | LPINFO | URL Encoded (Pipe) | 표준 LPINFO 형식 |
| expressvpn.com | A100637853 | A100637853... | data1 | URL Encoded (Pipe) | Impact-linkprice aid |
| credit.co.kr | A100637853 | A100637853... | LPINFO | Plain Text (Pipe) | 표준 LPINFO 형식 |

### 자체 제휴 시스템 사용 사이트

| 사이트 | 제휴 ID | 쿠키값 (앞 10자리) | 쿠키명 | 암호화 방식 | 비고 |
|--------|---------|-------------------|--------|-------------|------|
| lotteon.com | 294653448 | 294653448s... | ch_mem_no | Plain Text | 롯데온 자체 시스템 |
| coupang.com | AF9743124 | AF9743124,... | trac_lptag | Plain Text | 쿠팡파트너스 |
| ko.aliexpress.com | 789531147 | - | xman_us_f | JSON (URL Encoded) | AliExpress 자체 시스템 (affiliateKey: _c3kMBO5R) |
| nordvpn.com | 113701 | - | aff_id | Plain Text | NordVPN 자체 제휴 시스템 |

### 미작동 사이트

| 사이트 | 상태 | 비고 |
|--------|------|------|
| agoda.com | 미작동 | LinkPrice 쿠키 없음 |
| hotelscombined.co.kr | 미작동 | LinkPrice 쿠키 없음 |

---

## 쿠키 형식 분류

### 1. 표준 LPINFO 형식
```
A100637853|trackingCode|0000|B|1
```
- yes24, 교보문고, 인터파크, 하이마트, 99플라워, 무툰, 티스테이션, 땅, 크레딧

### 2. Base64 JSON 형식 (Gmarket)
```json
{
  "tokenType": "Channel",
  "inflowSeq": 1234567890,
  "inflowChannelToken": "A100637853",
  "channelMediaType": "LINK_PRICE",
  "serviceCode": "GMARKET",
  "created": 1234567890000,
  "subId": null
}
```

### 3. PARTNERSHIP 형식 (Auction)
```
A100637853|trackingCode|0000|B|1
```

### 4. XSITE 형식 (11st)
```
A100637853|trackingCode|0000|B|1
```

---

## 참고사항

- **주요 제휴 ID**: `A100637853` (LinkPrice 표준)
- **쿠팡파트너스 ID**: `AF9743124`
- **롯데온 ID**: `294653448`
- **NordVPN ID**: `113701`
- **AliExpress Key**: `_c3kMBO5R`
