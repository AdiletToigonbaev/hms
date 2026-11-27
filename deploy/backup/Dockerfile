# syntax=docker/dockerfile:1
# Резервное копирование HMS: pg_dump той же мажорной версии, что и БД (PostgreSQL 18),
# шифрование age (публичным ключом — сервер не может расшифровать свои бэкапы), выгрузка вне VPS — rclone.
FROM postgres:18-alpine
RUN apk add --no-cache age rclone tzdata
COPY --chmod=755 common.sh backup.sh restore.sh schedule.sh /usr/local/lib/hms-backup/
COPY --chmod=755 hms-backup /usr/local/bin/hms-backup
ENV TZ=Asia/Bishkek \
    BACKUP_DIR=/backups \
    FILES_DIR=/files
ENTRYPOINT ["hms-backup"]
CMD ["schedule"]
