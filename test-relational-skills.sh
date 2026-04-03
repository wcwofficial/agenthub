#!/bin/bash
echo "=== Тестирование реляционной модели с AgentSkills ==="

# Ждем запуска API
echo "Ожидание запуска API..."
sleep 10

# 1. Регистрация агента с SkillDetails
echo -e "\n1. Регистрация агента с разными rates для skills:"
response=$(curl -s -X POST http://localhost:8080/api/agents/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Никита + Степан 🦥 (реляционная)",
    "roles": ["worker", "assistant"],
    "description": "Human-AI команда (реляционная БД)",
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

# 2. Получение профиля агента (должны быть SkillDetails из AgentSkills)
echo -e "\n2. Получение профиля (должны быть SkillDetails из таблицы AgentSkills):"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '.skillDetails' 2>/dev/null || curl -s "http://localhost:8080/api/agents/$AGENT_ID"

# 3. Проверим БД напрямую через Adminer (порт 8080)
echo -e "\n3. Проверка структуры БД:"
echo "Adminer доступен на: http://localhost:8080/?pgsql=postgres&username=agenthub&db=agenthub"
echo "Пароль из .env файла"

# 4. Замена навыков: PUT /skills (профиль PATCH трогает только поля агента, не AgentSkills)
echo -e "\n4. Обновление навыков (PUT /skills):"
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
  }' 2>&1 | tail -5

# 5. Проверка обновленного профиля
echo -e "\n5. Проверка обновленного профиля:"
curl -s "http://localhost:8080/api/agents/$AGENT_ID" | jq '{name, skillDetails: .skillDetails | map({skill, amount, location})}' 2>/dev/null || echo "Ошибка"

# 6. Создадим еще одного агента для теста поиска
echo -e "\n6. Создание второго агента:"
curl -s -X POST http://localhost:8080/api/agents/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Тестовый агент",
    "roles": ["worker"],
    "skillDetails": [
      {
        "skill": "design",
        "currency": "USD",
        "amount": 40,
        "location": "remote"
      }
    ],
    "contactMode": 0
  }' | jq '{id, name}' 2>/dev/null || echo "Создан"

# 7. Поиск агентов по skill
echo -e "\n7. Поиск агентов с skill 'coding':"
curl -s "http://localhost:8080/api/agents/search?skill=coding" | jq '.[0] | {name, location: .skillDetails?[0]?.location}' 2>/dev/null || echo "Результаты поиска"

echo -e "\n=== Тест завершен ==="
echo "Проверь БД через Adminer: таблицы Agents и AgentSkills должны быть связаны"