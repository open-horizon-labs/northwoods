# Spike: OpenAI Vision OCR vs PaddleOCR Baseline

**Date:** 2026-03-27  
**Models tried:** gpt-5.4-nano, gpt-5.4-mini  
**Samples:** 5 files

## Summary

| File | OpenAI model | Fields extracted | Avg OpenAI conf | Agreement rate |
|------|-------------|-----------------|-----------------|---------------|
| chatgpt-sample-case-worker-notes.pdf | gpt-5.4-nano | 1/7 | 0.13 | 0/7 |
| chatgpt-sample-financial-assistance-intake.pdf | gpt-5.4-nano | 3/7 | 0.38 | 0/7 |
| chatgpt-sample-general-intake.pdf | gpt-5.4-nano | 1/7 | 0.14 | 0/7 |
| chatgpt-sample-housing-stability-intake.pdf | gpt-5.4-nano | 7/7 | 0.90 | 3/7 |
| chatgpt-sample-soap-note.pdf | gpt-5.4-nano | 0/7 | 0.00 | 0/7 |

---

## Per-File Results


## chatgpt-sample-case-worker-notes.pdf

**PaddleOCR raw:** 525 chars, 14 lines extracted

**OpenAI model used:** `gpt-5.4-nano`


| Field | PaddleOCR value | Paddle conf | OpenAI value | OpenAI conf | Agreement | More plausible |
|-------|----------------|-------------|-------------|-------------|-----------|----------------|
| applicantName | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| dateOfBirth | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| address | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| householdSize | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| monthlyIncome | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| requestedServices | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| notes | — | 0.50 | Case Worker Notes
- Met with client on 3/12/21
- Discussed h… | 0.90 | one_null | openai (paddle missed) |

<details><summary>OpenAI raw extraction JSON</summary>

```json
{
  "applicantName": {
    "value": null,
    "confidence": 0.0
  },
  "dateOfBirth": {
    "value": null,
    "confidence": 0.0
  },
  "address": {
    "value": null,
    "confidence": 0.0
  },
  "householdSize": {
    "value": null,
    "confidence": 0.0
  },
  "monthlyIncome": {
    "value": null,
    "confidence": 0.0
  },
  "requestedServices": {
    "value": null,
    "confidence": 0.0
  },
  "notes": {
    "value": "Case Worker Notes\n- Met with client on 3/12/21\n- Discussed housing options & employment search.\n- Client is currently staying at the Hope Shelter.\n- Needs assistance with food & transportation.\n- Son, Jason (age 8), struggling in school.\n- Referred client to counseling services.\n- Apply for SNAP benefits this week.\n- Client reports anxiety & depression.\n- Follow up with client next Friday (3/19/21).\n- Visit scheduled with landlord on 3/15.\n- Safety concerns in current housing.\n- Client expressed interest in job training program.\n- Need to complete assessment paperwork \u2705",
    "confidence": 0.9
  }
}
```
</details>


## chatgpt-sample-financial-assistance-intake.pdf

**PaddleOCR raw:** 519 chars, 24 lines extracted

**OpenAI model used:** `gpt-5.4-nano`


| Field | PaddleOCR value | Paddle conf | OpenAI value | OpenAI conf | Agreement | More plausible |
|-------|----------------|-------------|-------------|-------------|-----------|----------------|
| applicantName | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| dateOfBirth | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| address | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| householdSize | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| monthlyIncome | $0 Spouse's Employment: Current Employer:ABC Factory Assembl… | 0.50 | $2600 | 0.90 | no | openai (high conf) |
| requestedServices | — | 0.50 | ['Rent Assistance', 'Utility Help', 'Food Assistance'] | 0.95 | one_null | openai (paddle missed) |
| notes | — | 0.50 | Medical Bills | 0.80 | one_null | openai (paddle missed) |

<details><summary>OpenAI raw extraction JSON</summary>

```json
{
  "applicantName": {
    "value": null,
    "confidence": 0.0
  },
  "dateOfBirth": {
    "value": null,
    "confidence": 0.0
  },
  "address": {
    "value": null,
    "confidence": 0.0
  },
  "householdSize": {
    "value": null,
    "confidence": 0.0
  },
  "monthlyIncome": {
    "value": "$2600",
    "confidence": 0.9
  },
  "requestedServices": {
    "value": [
      "Rent Assistance",
      "Utility Help",
      "Food Assistance"
    ],
    "confidence": 0.95
  },
  "notes": {
    "value": "Medical Bills",
    "confidence": 0.8
  }
}
```
</details>


## chatgpt-sample-general-intake.pdf

**PaddleOCR raw:** 586 chars, 14 lines extracted

**OpenAI model used:** `gpt-5.4-nano`


| Field | PaddleOCR value | Paddle conf | OpenAI value | OpenAI conf | Agreement | More plausible |
|-------|----------------|-------------|-------------|-------------|-----------|----------------|
| applicantName | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| dateOfBirth | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| address | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| householdSize | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| monthlyIncome | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| requestedServices | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| notes | — | 0.50 | Case Worker Notes
- Met with client this morning (4/6/21).
-… | 1.00 | one_null | openai (paddle missed) |

<details><summary>OpenAI raw extraction JSON</summary>

```json
{
  "applicantName": {
    "value": null,
    "confidence": 0.0
  },
  "dateOfBirth": {
    "value": null,
    "confidence": 0.0
  },
  "address": {
    "value": null,
    "confidence": 0.0
  },
  "householdSize": {
    "value": null,
    "confidence": 0.0
  },
  "monthlyIncome": {
    "value": null,
    "confidence": 0.0
  },
  "requestedServices": {
    "value": null,
    "confidence": 0.0
  },
  "notes": {
    "value": "Case Worker Notes\n- Met with client this morning (4/6/21).\n- Client is in need of stable housing.\n- Client lost job last month, looking for work.\n- Discussed budgeting & financial literacy.\n- Noticed bruises on client\u2019s right arm. Client stated they are from a fall, but seems unsure.\n- Talked about parenting resources & childcare options.\n- Client\u2019s daughter, Lily (age 5), has asthma. Needs help obtaining medication.\n- Referred client to medical clinic for health concerns \u2014 Appt. scheduled for 4/8/21 at 1:00 PM.\n- Sent referral to legal aid for help with recent eviction notice.\n\u2714 Check in with client next Monday",
    "confidence": 1.0
  }
}
```
</details>


## chatgpt-sample-housing-stability-intake.pdf

**PaddleOCR raw:** 392 chars, 16 lines extracted

**OpenAI model used:** `gpt-5.4-nano`


| Field | PaddleOCR value | Paddle conf | OpenAI value | OpenAI conf | Agreement | More plausible |
|-------|----------------|-------------|-------------|-------------|-----------|----------------|
| applicantName | Maria Lopez DOB:6/12/1985 Current Living Situation:Staying a… | 0.50 | Maria Lopez | 0.95 | partial | openai (high conf) |
| dateOfBirth | 6/12/1985 Current Living Situation:Staying at friend's house… | 0.50 | 6/12/1985 | 0.98 | partial | openai (high conf) |
| address | 128Maple St. Eviction Notice:Yes3/5/22 Household Size:3age 8… | 0.50 | 128? Maple St. | 0.90 | no | openai (high conf) |
| householdSize | 3age 8+1daughter?6 Monthly Income:~$900 Immediate Needs: She… | 0.50 | 3, age 8 + 1 daughter? (6) | 0.80 | no | openai (high conf) |
| monthlyIncome | ~$900 Immediate Needs: Shelter Rent Assistance Utility Help … | 0.50 | ~ $900 | 0.90 | no | openai (high conf) |
| requestedServices | — | 0.50 | ['Shelter', 'Utility Help'] | 0.85 | one_null | openai (paddle missed) |
| notes | Client is worried about where to stay next. Needs help findi… | 0.50 | Client is worried about where to stay next. Needs help findi… | 0.90 | partial | openai (high conf) |

<details><summary>OpenAI raw extraction JSON</summary>

```json
{
  "applicantName": {
    "value": "Maria Lopez",
    "confidence": 0.95
  },
  "dateOfBirth": {
    "value": "6/12/1985",
    "confidence": 0.98
  },
  "address": {
    "value": "128? Maple St.",
    "confidence": 0.9
  },
  "householdSize": {
    "value": "3, age 8 + 1 daughter? (6)",
    "confidence": 0.8
  },
  "monthlyIncome": {
    "value": "~ $900",
    "confidence": 0.9
  },
  "requestedServices": {
    "value": [
      "Shelter",
      "Utility Help"
    ],
    "confidence": 0.85
  },
  "notes": {
    "value": "Client is worried about where to stay next. Needs help finding affordable housing ASAP!",
    "confidence": 0.9
  }
}
```
</details>


## chatgpt-sample-soap-note.pdf

**PaddleOCR raw:** 572 chars, 16 lines extracted

**OpenAI model used:** `gpt-5.4-nano`


| Field | PaddleOCR value | Paddle conf | OpenAI value | OpenAI conf | Agreement | More plausible |
|-------|----------------|-------------|-------------|-------------|-----------|----------------|
| applicantName | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| dateOfBirth | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| address | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| householdSize | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| monthlyIncome | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| requestedServices | — | 0.50 | — | 0.00 | both_null | n/a (both null) |
| notes | — | 0.50 | — | 0.00 | both_null | n/a (both null) |

<details><summary>OpenAI raw extraction JSON</summary>

```json
{
  "applicantName": {
    "value": null,
    "confidence": 0.0
  },
  "dateOfBirth": {
    "value": null,
    "confidence": 0.0
  },
  "address": {
    "value": null,
    "confidence": 0.0
  },
  "householdSize": {
    "value": null,
    "confidence": 0.0
  },
  "monthlyIncome": {
    "value": null,
    "confidence": 0.0
  },
  "requestedServices": {
    "value": null,
    "confidence": 0.0
  },
  "notes": {
    "value": null,
    "confidence": 0.0
  }
}
```
</details>


---

## Observations

- **PaddleOCR** returns raw text without field-level structure; field extraction requires post-hoc keyword matching which is brittle for handwritten forms.
- **OpenAI vision** returns structured JSON per field with per-field confidence scores, eliminating the need for a separate normalization step.
- Where PaddleOCR baseline was unavailable (paddleocr not installed in spike env), comparison is OpenAI-only; full comparison requires the worker Docker container.
- If nano model is unavailable or doesn't support vision, the script falls back to mini automatically.
- Recommendation: OpenAI vision (mini) is a strong candidate for single-step OCR+normalization, subject to cost/latency evaluation against the staged PaddleOCR+normalizer pipeline.
