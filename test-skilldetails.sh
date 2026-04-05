#!/bin/bash
echo "=== SkillDetails model smoke test ==="

# 1. Register agent with skillDetails
echo -e "\n1. Register agent with per-skill rates:"
response=$(curl -s -X POST http://localhost:8080/api/agents/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Demo team",
    "roles": ["provider"],
    "description": "Human-AI team",
    "skillDetails": [
      {
        "skill": "coding",
        "currency": "USD",
        "amount": 50,
        "location": "remote",
        "availability": "9:00-18:00 UTC+3",
        "experienceLevel": 5
      },
      {
        "skill": "loaders",
        "currency": "USD",
        "amount": 30,
        "location": "Minsk",
        "availability": "10:00-20:00 UTC+3",
        "experienceLevel": 3
      },
      {
        "skill": "web_search",
        "currency": "USD",
        "amount": 20,
        "location": "remote",
        "availability": "24/7",
        "experienceLevel": 4
      }
    ],
    "contactMode": 0
  }')

echo "$response" | jq . 2>/dev/null || echo "$response"

API_KEY=$(echo "$response" | grep -o '"apiKey":"[^"]*"' | cut -d'"' -f4)
AGENT_ID=$(echo "$response" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)

echo -e "\nAPI Key: $API_KEY"
echo "Agent ID: $AGENT_ID"

# 2. Fetch agent profile
echo -e "\n2. Fetch profile (expect skillDetails):"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '.skillDetails' 2>/dev/null || curl -s "http://localhost:8080/api/agents/$AGENT_ID"

# 3. Replace skills: PUT .../skills + Bearer
echo -e "\n3. Replace skills (PUT .../skills, full list):"
curl -sS -X PUT "http://localhost:8080/api/agents/$AGENT_ID/skills" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d '{
    "skillDetails": [
      {
        "skill": "coding",
        "currency": "USD",
        "amount": 55,
        "location": "remote"
      },
      {
        "skill": "loaders",
        "currency": "USD",
        "amount": 35,
        "location": "Minsk"
      },
      {
        "skill": "web_search",
        "currency": "USD",
        "amount": 25,
        "location": "remote"
      },
      {
        "skill": "deployment",
        "currency": "USD",
        "amount": 60,
        "location": "remote",
        "notes": "Docker, Kubernetes, CI/CD"
      }
    ]
  }' | jq . 2>/dev/null || true

# 4. Verify updated profile
echo -e "\n4. Verify updated profile:"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '{name, skillDetails}' 2>/dev/null || echo "Request failed"

# 5. Search by skill
echo -e "\n5. Search agents with skill coding:"
curl -s "http://localhost:8080/api/agents/search?skill=coding" | jq '.[0] | {name, skillDetails}' 2>/dev/null || echo "Search failed"

echo -e "\n=== Test finished ==="
