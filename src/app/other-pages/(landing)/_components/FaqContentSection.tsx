import React, { useState } from "react";
import { Button, Card, CardBody, Col, Container, Row } from "reactstrap";
import { IconBook2, IconHeadphones } from "@tabler/icons-react";
import SectionHeading from "@/app/other-pages/(landing)/_components/SectionHeading";

const FaqContentSection = () => {
  const [openAccordion, setOpenAccordion] = useState<string | null>(
    "nestingcollapseOne"
  );

  const toggleAccordion = (id: string) => {
    setOpenAccordion(openAccordion === id ? null : id);
  };

  return (
    <div>
      <Container>
        <SectionHeading
          title="Inquired"
          highlight="Queries"
          description="After reading the instructions, I had a few inquired queries
                about the process and decided to reach out to customer support
                for clarification."
        />

        <Row className="align-items-center">
          <Col lg={4}>
            <Row>
              {/* Digitize Your Documents Card */}
              <Col lg="12" sm="6" className="mb-3">
                <Card className="card-boxs">
                  <CardBody>
                    <div className="icon-boxs">
                      <IconBook2 size={18} />
                    </div>
                    <div className="box-content">
                      <h4>Digitize Your Documents</h4>
                      <p>Efficiently Arranged and Current</p>
                      <Button
                        color="primary"
                        target="_blank"
                        href="https://polytronx.com/support"
                      >
                        Open Guide
                      </Button>
                    </div>
                  </CardBody>
                </Card>
              </Col>

              {/* Committed Assistance Card */}
              <Col lg="12" sm="6">
                <Card className="card-boxs">
                  <CardBody>
                    <div className="icon-boxs">
                      <IconHeadphones size={18} />
                    </div>
                    <div className="box-content">
                      <h4>Committed Assistance</h4>
                      <p>
                        Require assistance? Send us a ticket. We&#39;re here to
                        help!
                      </p>
                      <Button
                        color="primary"
                        target="_blank"
                        href="mailto:support@polytronx.com"
                      >
                        Get Support
                      </Button>
                    </div>
                  </CardBody>
                </Card>
              </Col>
            </Row>
          </Col>
          <Col lg={8}>
            <div className="landing-accordion">
              <div className="accordion app-accordion accordion-flush accordion-light-dark app-accordion-plus">
                {[
                  {
                    id: "nestingcollapseOne",
                    question: "What is the role of an admin??",
                    answer:
                      "Admins are responsible for managing and overseeing the smooth operation of a system, platform, or organization.",
                  },
                  {
                    id: "nestingcollapseTwo",
                    question: "How do I add or remove users?",
                    answer:
                      "Depending on the system, user management can usually be done through the admin dashboard. Look for the <strong>'User Management'</strong> or <strong>'Admin Settings'</strong> section, where you can add or remove users and assign roles.",
                  },
                  {
                    id: "nestingcollapseFour",
                    question: "What security measures should I implement?",
                    answer:
                      "Admins should prioritize security by enforcing strong password policies, implementing two-factor authentication, regularly updating software, and monitoring system logs for any suspicious activities.",
                  },
                  {
                    id: "nestingcollapseFive",
                    question: "How can I troubleshoot common issues?",
                    answer:
                      "Document and follow a systematic approach to troubleshooting. Check error logs, consult documentation, and involve relevant stakeholders if needed.",
                  },
                  {
                    id: "nestingcollapseSix",
                    question:
                      "How can I stay informed about updates and patches?",
                    answer:
                      "Subscribe to official newsletters, forums, or mailing lists related to the software or system you're administering. Regularly check the official website for announcements and security patches. Stay informed about the latest industry trends and best practices.",
                  },
                  {
                    id: "nestingcollapseSeven",
                    question: "What is the process for system upgrades?",
                    answer:
                      "Before upgrading, thoroughly review release notes, test the upgrade in a non-production environment, and ensure compatibility with existing plugins or integrations.",
                  },
                  {
                    id: "nestingcollapseEight",
                    question: "How do I handle user access permissions?",
                    answer:
                      "Project timelines vary based on scope and complexity, so it is best to plan releases in phases and validate each step before rollout.",
                  },
                ].map((item) => (
                  <div className="accordion-item" key={item.id}>
                    <h2 className="accordion-header">
                      <button
                        className={`accordion-button ${openAccordion === item.id ? "" : "collapsed"}`}
                        type="button"
                        onClick={() => toggleAccordion(item.id)}
                      >
                        {item.question}
                      </button>
                    </h2>
                    <div
                      className={`accordion-collapse collapse ${openAccordion === item.id ? "show" : ""}`}
                    >
                      <div className="accordion-body">
                        <p>{item.answer}</p>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default FaqContentSection;
