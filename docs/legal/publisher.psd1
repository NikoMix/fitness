<#
    Publisher details for Forge's legal documents.

    Everything here is a fact only the publisher can supply. The legal documents under docs/legal
    carry TODO(owner: ...) markers wherever one is needed, and Build-LegalSite.ps1 substitutes the
    values below into both the published site and the in-app copy.

    HOW TO USE THIS FILE
      1. Fill in the values. Leave anything you do not have yet as an empty string.
      2. Run:  pwsh tools/legal/Set-PublisherDetails.ps1
      3. That regenerates the in-app legal copy and tells you exactly what is still missing.

    WHY THE VALUES ARE NOT GUESSED
    A privacy policy is a legal commitment. A wrong legal entity or the wrong governing law is not
    a placeholder that can be tidied up later, it is a false statement in a document users and
    regulators are entitled to rely on. An empty value therefore keeps its visible TODO marker, and
    tools/ci/Test-NoOwnerPlaceholders.ps1 blocks a store build while any marker would ship. A
    reviewer who opens the privacy policy and reads "TODO for the publisher" is a certain
    rejection - and for Google Play that restarts a Health Apps review measured in weeks.
#>
@{
    # -----------------------------------------------------------------------------------------
    # Identity
    # -----------------------------------------------------------------------------------------

    # The registered legal entity that publishes Forge. A trading name is not enough: this is the
    # entity a user would name in a complaint or a data-subject request.
    # Example: 'NikoMix Ltd'
    LegalEntity          = ''

    # Company registration number, if the entity has one. Leave empty for a sole trader.
    # Example: '12345678'
    RegistrationNumber   = ''

    # Registered postal address, on one line. Required by GDPR Article 13 for the data controller,
    # and Apple and Google both expect a real address rather than a PO box for a health app.
    # Example: '1 Example Street, London, EC1A 1AA, United Kingdom'
    PostalAddress        = ''

    # -----------------------------------------------------------------------------------------
    # Contact addresses
    #
    # These may all be the same mailbox. What matters is that it is monitored: the privacy policy
    # promises a response, and an unanswered erasure request is a GDPR breach rather than poor
    # service.
    # -----------------------------------------------------------------------------------------

    SupportEmail         = ''
    PrivacyEmail         = ''
    DeletionEmail        = ''
    SecurityEmail        = ''

    # Contact for the terms of service. Left empty it falls back to PrivacyEmail, because for a
    # solo publisher these are the same mailbox and blocking a release on the distinction would be
    # bureaucracy rather than diligence.
    LegalEmail           = ''

    # How quickly you undertake to respond. Promise something you can keep on a bad week; GDPR
    # gives one month for a data-subject request, so anything under that is a commitment you are
    # choosing to make.
    # Example: 'five working days'
    ResponseWindow       = ''

    # -----------------------------------------------------------------------------------------
    # Jurisdiction
    # -----------------------------------------------------------------------------------------

    # Governing law for the terms of service.
    # Example: 'the laws of England and Wales'
    GoverningLaw         = ''

    # Courts with jurisdiction over disputes.
    # Example: 'the courts of England and Wales'
    Courts               = ''

    # The data protection supervisory authority a user can complain to. This follows from where the
    # entity is established, not from where the user lives.
    # Example: 'the Information Commissioner''s Office (ICO) in the United Kingdom'
    SupervisoryAuthority = ''

    # -----------------------------------------------------------------------------------------
    # Published site
    # -----------------------------------------------------------------------------------------

    # The public URL the privacy policy will live at once GitHub Pages is enabled. Both stores
    # require a URL that loads with no login, and Play additionally needs a data-deletion URL.
    # Example: 'https://nikomix.github.io/fitness/privacy/'
    PrivacyPolicyUrl     = ''
}
