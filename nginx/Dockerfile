FROM nginx:1.27-alpine
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY site /usr/share/nginx/html
COPY docs/AGENT_INTEGRATORS.md /usr/share/nginx/html/AGENT_INTEGRATORS.md
COPY docs/openclaw-skill-template/SKILL.md /usr/share/nginx/html/skill-template.md
COPY skills/agenthub/SKILL.md /usr/share/nginx/html/openclaw-agenthub-skill.md
