# SHIELD Principle: SurveyManager's Guide

## Understanding SHIELD

**SHIELD** (Separate Human Identities Entirely from Linked Data) is the core privacy principle of our survey system. As an survey manager or administrator, it's essential you understand both its purpose and your responsibilities in maintaining it.

### The Restaurant Kitchen Analogy

Think of our SHIELD system like a restaurant with a special setup:

- You, as the **restaurant manager**, know which customers have made reservations
- The **kitchen staff** prepares meals based on orders that come through a digital system
- The chefs see "Table 7 ordered pasta" but never see customer names or faces
- When the food is ready, servers deliver it to the right table number
- At the end, you know "all reserved customers were served" without the kitchen knowing who ate what
- Customer satisfaction surveys are submitted anonymously, so feedback is honest

As a survey manager, you're like the restaurant manager—you know who's participating and can see overall satisfaction, but the specific connections between individuals and their responses remain deliberately separated, just as the kitchen staff never knows exactly who they're cooking for.

## What SHIELD Means for SurveyManagers

### Complete Separation

The system maintains complete separation between:
* **Identity data** (who participated)
* **Response data** (what answers were given)

This separation is not merely a feature—it is the foundational architecture of the entire system.

### Your Role as an SurveyManager

As a survey manager, you:
* **Can** see who has participated in surveys
* **Can** see aggregated, anonymized response data
* **Cannot** connect specific responses to specific participants
* **Cannot** override this separation (by design)

This isn't a limitation of your permissions—it's a deliberate architectural choice that protects both participants and survey managers.

## Practical Administration Under SHIELD

### Managing Surveys

When creating and managing surveys:
* Entry codes provide anonymous access
* Completion codes verify participation
* Neither code type allows connecting identities to answers

### Reporting Capabilities

The reporting system provides:
* Participation rates and completion statistics
* Aggregated response data and trends
* Demographic breakdowns (when configured)
* **Never** individual-level response tracking

### Answering Common Questions

#### "Can we track who said what?"
**Answer**: "No, and this is by design. The system physically separates identity from responses, making it impossible to link them."

#### "How do we know if a specific person completed the survey?"
**Answer**: "The system tracks participation separately from responses. We can confirm someone participated without knowing their specific answers."

#### "Can we get raw data with identifiers for research purposes?"
**Answer**: "The system doesn't store identifiers with response data. Research can be conducted on the anonymized data while maintaining participant privacy."

## Administrative Responsibilities

As a survey manager, you should:

1. **Communicate clearly** about SHIELD protection to participants
2. **Never attempt to circumvent** the separation (e.g., by adding identifying questions)
3. **Respect the architecture** of the system in your administrative practices
4. **Follow proper shutdown procedures** by stopping all active surveys before shutting down or restarting the server
5. **Wait for delayed submissions** to complete processing before system shutdown to prevent data loss
6. **Report any concerns** about potential SHIELD violations immediately

> **IMPORTANT:** The Delayed Submission Service does not automatically flush pending responses during system shutdown. This is an intentional security feature that prevents administrators from correlating submissions with participant identities. Always properly stop surveys and wait for all submissions to process before shutting down. Administrators can monitor the status of delayed submissions on the Admin → DSS Status page.

## Benefits of SHIELD for Organizations

* **Increased response rates** due to participant confidence
* **More honest feedback** when participants know they can't be identified
* **Reduced liability** around sensitive data
* **Simplified compliance** with privacy regulations
* **Better data quality** through respondent trust

## Getting Support

If you have questions about administering the system while maintaining SHIELD compliance:
* Review the technical documentation
* Contact the system administrator
* Submit a support ticket for specific questions

---

*This guide should be reviewed by all survey managers and administrators as part of their onboarding process.*