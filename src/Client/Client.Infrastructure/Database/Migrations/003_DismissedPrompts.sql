-- The questions the user asked not to be shown again, by their keys; a question that is not
-- listed is asked. The question on closing the application is not here, it is a behavior setting
CREATE TABLE DismissedPrompts (
    PromptKey TEXT NOT NULL PRIMARY KEY
) STRICT;
