import React, { useState } from "react";
import {
  Accordion,
  AccordionBody,
  AccordionHeader,
  AccordionItem,
  Card,
  CardBody,
  CardHeader,
  Col,
  Row,
  UncontrolledCollapse,
} from "reactstrap";
import {
  IconMessageReport,
  IconTicket,
  IconTransform,
  IconCode,
  IconPlus,
  IconMinus,
} from "@tabler/icons-react";

const faqData = [
  {
    id: "1",
    icon: <IconMessageReport size={20} className="me-1 f-s-20" />,
    question: "What happens if I just pay my ticket?",
    answer:
      "Paying your ticket without contesting it can result in a conviction on your driving record. This can have long-lasting consequences such as increased insurance rates, driver's license suspension, employment restrictions, and additional fines imposed by your state's DMV.",
  },
  {
    id: "2",
    icon: <IconTicket size={20} className="me-1 f-s-20" />,
    question: "Can this ticket affect my job?",
    answer:
      "Absolutely, tickets recorded on your driving history can result in disqualification from driving commercially or obtaining a commercial driver's license (CDL). They can also disqualify you from driving for ride-sharing services such as Uber or Lyft, which require a clean driving record.",
  },
  {
    id: "3",
    icon: <IconTransform size={20} className="me-1 f-s-20" />,
    question: "How long does it take to resolve my case?",
    answer:
      "It really depends on the court your ticket landed in. Some courts move faster than others, but on average, it could take about 1-3 months. This is perfectly normal in the legal process.",
  },
];

const FaqAccordions = () => {
  const [openOutline, setOpenOutline] = useState<string>("1");
  const [openLeftIcon, setOpenLeftIcon] = useState<string>("1");

  const toggleOutline = (id: string) =>
    setOpenOutline(openOutline === id ? "" : id);

  const toggleLeftIcon = (id: string) =>
    setOpenLeftIcon(openLeftIcon === id ? "" : id);

  return (
    <Row>
      {/* Outline Accordion */}
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="txt-ellipsis mb-0">
              Frequently Asked Questions (Real Example)
            </h5>
            <a href="#" id="togglerAsk">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Accordion
              open={openOutline}
              toggle={toggleOutline}
              className="accordion app-accordion accordion-light-secondary"
            >
              {faqData.map((item) => (
                <AccordionItem key={item.id}>
                  <AccordionHeader targetId={item.id}>
                    {item.icon}
                    {item.question}
                  </AccordionHeader>
                  <AccordionBody accordionId={item.id}>
                    {item.answer}
                  </AccordionBody>
                </AccordionItem>
              ))}
            </Accordion>

            <UncontrolledCollapse toggler="#togglerAsk">
              <pre>
                <code className="language-html">
                  {`<Accordion className="accordion-icon" defaultOpen="1">
${faqData
  .map(
    (item, index) => `
  <AccordionItem key="${index}">
    <AccordionHeader targetId="${index + 1}">
      <i className="ti ${item.icon} me-1 f-s-20"></i> ${item.question}
    </AccordionHeader>
    <AccordionBody accordionId="${index + 1}">
      ${item.answer}
    </AccordionBody>
  </AccordionItem>`
  )
  .join("\n")}
</Accordion>`}
                </code>
              </pre>
            </UncontrolledCollapse>
          </CardBody>
        </Card>
      </Col>

      {/* Left Icon Plus/Minus Accordion */}
      <Col lg={6}>
        <Card className="equal-card">
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="txt-ellipsis mb-0">Left Side Plus Minus Icon</h5>
            <a href="#" id="togglerPlus">
              <IconCode className="source cursor-pointer" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Accordion
              open={openLeftIcon}
              toggle={toggleLeftIcon}
              className="accordion app-accordion accordion-light-primary"
            >
              {faqData.map((item) => (
                <AccordionItem key={item.id}>
                  <AccordionHeader targetId={item.id}>
                    <span className="me-2">
                      {openLeftIcon === item.id ? (
                        <IconMinus size={16} />
                      ) : (
                        <IconPlus size={16} />
                      )}
                    </span>
                    {item.question}
                  </AccordionHeader>
                  <AccordionBody accordionId={item.id}>
                    {item.answer}
                  </AccordionBody>
                </AccordionItem>
              ))}
            </Accordion>

            <UncontrolledCollapse toggler="#togglerPlus">
              <pre>
                <code className="language-html">
                  {`<Accordion className="accordion-icon" defaultOpen="1">
${faqData
  .map((item, index) => {
    return `
  <AccordionItem key="${index}">
    <AccordionHeader targetId="${index + 1}">
      ${index === 0 ? "<i class='ti ti-message-report me-1 f-s-20'></i>" : ""} ${item.question}
    </AccordionHeader>
    <AccordionBody accordionId="${index + 1}">
      ${item.answer}
    </AccordionBody>
  </AccordionItem>`;
  })
  .join("\n")}
</Accordion>`}
                </code>
              </pre>
            </UncontrolledCollapse>
          </CardBody>
        </Card>
      </Col>
    </Row>
  );
};

export default FaqAccordions;
