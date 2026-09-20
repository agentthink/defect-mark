FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV MASKFLOW_DB=/data/maskflow.sqlite3
ENV MASKFLOW_STORAGE=/data/storage
ENV MASKFLOW_FREE_BYTES=1073741824
ENV SAM_SERVICE_URL=http://sam-service:8001

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]

