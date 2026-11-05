-- Adds the Executive role to an existing chatbot.UserAccess table (001 already includes it on fresh installs).
-- Run once as a login that owns the chatbot schema.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_UserAccess_Role' AND parent_object_id = OBJECT_ID('chatbot.UserAccess'))
    ALTER TABLE chatbot.UserAccess DROP CONSTRAINT CK_UserAccess_Role;
ALTER TABLE chatbot.UserAccess WITH CHECK
    ADD CONSTRAINT CK_UserAccess_Role CHECK (Role IN ('BillingStaff', 'RcmManager', 'Analyst', 'Executive'));
