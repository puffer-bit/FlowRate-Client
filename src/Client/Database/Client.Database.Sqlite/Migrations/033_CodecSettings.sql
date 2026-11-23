ALTER TABLE VideoSettings ADD COLUMN Vp8Encoder TEXT;
ALTER TABLE VideoSettings ADD COLUMN Vp9Encoder TEXT;
ALTER TABLE VideoSettings ADD COLUMN Vp8Decoder TEXT;
ALTER TABLE VideoSettings ADD COLUMN Vp9Decoder TEXT;

CREATE TABLE CodecParameters (
    Factory TEXT NOT NULL,
    Name TEXT NOT NULL,
    Value TEXT NOT NULL,
    PRIMARY KEY (Factory, Name)
);
INSERT INTO CodecParameters (Factory, Name, Value) SELECT Encoder, Name, Value FROM EncoderParameters;
DROP TABLE EncoderParameters;
