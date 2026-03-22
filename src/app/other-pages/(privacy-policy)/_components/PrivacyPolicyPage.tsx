import React from "react";
import { Card, CardBody, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconNews } from "@tabler/icons-react";

const PrivacyPolicyPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Privacy Policy"
          title=" Other Pages"
          path={["Privacy Policy"]}
          Icon={IconNews}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardBody>
                <h5 className="text-primary mt-3">
                  <i className="ti ti-north-star text-primary me-2"></i>
                  What information does PolytronX collect?
                </h5>
                <div className="mt-4">
                  <p className="text-dark f-s-16 mt-3">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    PolytronX is committed to protecting the privacy of teams,
                    administrators, and end users who interact with the
                    platform. This page explains the categories of information
                    that may be collected when you use PolytronX websites,
                    dashboards, forms, notifications, and related support
                    services.
                  </p>
                  <p className="text-dark f-s-16 mt-3">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    The information we collect is used to keep the platform
                    secure, improve performance, support account management, and
                    help teams operate the workspace effectively. If a feature
                    is optional, you can choose not to provide the related
                    details.
                  </p>
                  <p className="mt-3 text-dark f-s-16">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    Depending on how PolytronX is configured, we may collect the
                    following categories of information:
                  </p>
                  <ul className="px-5 py-3">
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Account details such as name, email address, role, and
                      organization information during sign in, registration, or
                      profile updates.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Contact details submitted through support forms, help
                      requests, feedback forms, or service notifications.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Workspace preferences such as language, layout settings,
                      theme choices, and dashboard configuration.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Operational data such as task activity, uploaded files,
                      comments, workflow history, and usage events generated
                      while using the platform.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Technical data including device details, browser type, IP
                      address, session diagnostics, and performance metrics used
                      for security and reliability.
                    </li>
                  </ul>
                  <h5 className="text-primary mt-3">
                    <i className="ti ti-north-star text-primary me-2"></i>
                    How is information collected?
                  </h5>
                  <p className="text-dark f-s-16 corner-arrow mt-3">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    Information is collected directly when you provide it,
                    automatically through normal platform activity, or through
                    workspace features that administrators enable for their
                    teams. This can include registration forms, profile updates,
                    file uploads, support requests, cookies, analytics, and
                    security monitoring tools.
                  </p>
                  <p className="text-dark f-s-16 corner-arrow mt-3">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    PolytronX uses reasonable safeguards to protect data and
                    only processes information required to operate, improve, and
                    support the platform. If you have questions about privacy or
                    data handling, please contact your workspace administrator
                    or the PolytronX support team.
                  </p>
                  <p className="text-primary text-end f-w-600 text-d-underline">
                    Last update: 23 Mar, 2026
                  </p>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default PrivacyPolicyPage;
