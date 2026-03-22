import React from "react";
import { Card, CardBody, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconNews } from "@tabler/icons-react";

const TermsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Terms & Conditions"
          title=" Other Pages"
          path={["Terms & Conditions"]}
          Icon={IconNews}
        />
        <Row className="terms-condition-container">
          <Col>
            <Card>
              <CardBody>
                <h4 className="text-primary">
                  <i className="ti ti-north-star text-primary me-2"></i>
                  Welcome to PolytronX
                </h4>
                <div className="mt-4">
                  <p className="text-dark f-s-16 corner-arrow">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    These Terms & Conditions explain the rules for accessing and
                    using the PolytronX platform. By using the workspace, you
                    agree to follow the policies, responsibilities, and usage
                    expectations defined by your organization and platform
                    administrators.
                  </p>
                  <p className="mt-3 text-dark f-s-16">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    Clear terms help teams understand how accounts, content,
                    data, notifications, and service access should be managed
                    across day-to-day operations.
                  </p>
                  <ul className="px-5 py-3">
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Users are responsible for keeping their account
                      credentials secure and for using the platform in a lawful,
                      professional manner.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Workspace administrators may manage permissions, remove
                      access, or update configuration when necessary to protect
                      the platform and their data.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Teams should not upload harmful code, misuse shared
                      resources, or attempt unauthorized access to data, users,
                      or connected services.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Service availability, releases, and feature updates may
                      change over time as PolytronX continues to improve the
                      platform.
                    </li>
                    <li className="diamond diamond-bullet-secondary f-s-14 text-secondary">
                      Organizations remain responsible for the accuracy,
                      legality, and ownership of the content they manage inside
                      their workspace.
                    </li>
                  </ul>
                  <p className="mt-3 text-dark f-s-16">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    Continued use of PolytronX after updates to these terms
                    means you accept the revised conditions. If a provision no
                    longer works for your organization, please stop using the
                    affected service and contact support for guidance.
                  </p>
                  <h5 className="text-primary mt-3">
                    <i className="ti ti-north-star text-primary me-2"></i>
                    Copyright & Trademark
                  </h5>
                  <p className="mt-3 text-dark f-s-16">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    The PolytronX name, logos, product labels, interface marks,
                    and related brand assets are protected intellectual property
                    owned by PolytronX or its licensors. Nothing on this site
                    grants permission to reuse those assets without prior
                    written approval.
                  </p>
                  <p className="mt-3 text-dark f-s-16">
                    <i className="ti ti-corner-down-right-double text-primary me-2"></i>
                    References to third-party names, products, or services are
                    included for compatibility, integration, or informational
                    purposes only and do not imply endorsement unless stated
                    otherwise.
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

export default TermsPage;
