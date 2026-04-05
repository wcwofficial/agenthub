#!/bin/bash
echo "=== Relational AgentSkills smoke test ==="

echo "Waiting for API..."
sleep 10

# 1. Register with skillDetails
echo -e "\n1. Register agent with varied skill rates:"
response=$(curl -s -X POST http://localhost:8080/api/agents/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Demo relational agent",
    "roles": ["provider"],
    "description": "Human-AI team (relational)",
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

# 2. Profile (skillDetails from store)
echo -e "\n2. Fetch profile (expect skillDetails):"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '.skillDetails' 2>/dev/null || curl -s "http://localhost:8080/api/agents/$AGENT_ID"

# 3. DB hint (Adminer if present)
echo -e "\n3. DB check hint:"
echo "If Adminer runs: http://localhost:8080/?pgsql=postgres&username=agenthub&db=agenthub"
echo "Password from your .env"

# 4. PUT skills
echo -e "\n4. Replace skills (PUT .../skills):"
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
  }' 2>&1 | tail -5

# 5. Verify
echo -e "\n5. Verify updated profile:"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '{name, skillDetails: .skillDetails | map({skill, amount, location})}' 2>/dev/null || echo "Request failed"

# 6. Second agent for search
echo -e "\n6. Register second agent:"
curl -s -X POST http://localhost:8080/api/agents/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Second test agent",
    "roles": ["provider"],
    "skillDetails": [
      {
        "skill": "design",
        "currency": "USD",
        "amount": 40,
        "location": "remote"
      }
    ],
    "contactMode": 0
  }' | jq '{id, name}' 2>/dev/null || echo "Created"

# 7. Search
echo -e "\n7. Search skill coding:"
curl -s "http://localhost:8080/api/agents/search?skill=coding" | jq '.[0] | {name, location: .skillDetails?[0]?.location}' 2>/dev/null || echo "Search failed"

echo -e "\n=== Test finished ==="
echo "If using Adminer: Agents and per-skill rows should reflect updates"
