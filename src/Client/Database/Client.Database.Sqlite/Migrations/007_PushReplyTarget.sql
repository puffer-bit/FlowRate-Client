-- Where and with what token a reply typed into a notification is sent, without a connection
ALTER TABLE PushKeys ADD COLUMN Address TEXT NULL;
ALTER TABLE PushKeys ADD COLUMN Port INTEGER NULL;
ALTER TABLE PushKeys ADD COLUMN ReplyToken TEXT NULL;
