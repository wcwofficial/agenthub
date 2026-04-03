#!/bin/bash
echo "=== Тестирование новой модели с SkillDetails ==="

# 1. Регистрация агента с SkillDetails
echo -e "\n1. Регистрация агента с разными rates для skills:"
response=$(curl -s -X POST http://localhost:8080/api/agents/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Никита + Степан 🦥",
    "roles": ["worker", "assistant"],
    "description": "Human-AI команда",
    "skills": ["coding", "грузчик", "web_search"],
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
        "skill": "грузчик",
        "currency": "USD", 
        "amount": 30,
        "location": "Минск",
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

# Извлечем API ключ и ID
API_KEY=$(echo "$response" | grep -o '"apiKey":"[^"]*"' | cut -d'"' -f4)
AGENT_ID=$(echo "$response" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)

echo -e "\nAPI Key: $API_KEY"
echo "Agent ID: $AGENT_ID"

# 2. Получение профиля агента
echo -e "\n2. Получение профиля (должны быть SkillDetails):"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '.skillDetails' 2>/dev/null || curl -s "http://localhost:8080/api/agents/$AGENT_ID"

# 3. Замена списка навыков (отдельно от профиля): PUT /api/agents/{id}/skills + Bearer
echo -e "\n3. Обновление навыков (PUT /skills, полная замена списка):"
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
        "skill": "грузчик", 
        "currency": "USD",
        "amount": 35,
        "location": "Минск"
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

# 4. Проверка обновленного профиля
echo -e "\n4. Проверка обновленного профиля:"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '{name, skillDetails}' 2>/dev/null || echo "Ошибка"

# 5. Поиск агентов по skill
echo -e "\n5. Поиск агентов с skill 'coding':"
curl -s "http://localhost:8080/api/agents/search?skill=coding" | jq '.[0] | {name, skillDetails}' 2>/dev/null || echo "Результаты поиска"

echo -e "\n=== Тест завершен ==="