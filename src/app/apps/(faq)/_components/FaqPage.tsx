"use client";
import React from "react";
import {
  InputGroup,
  Input,
  InputGroupText,
  Container,
  Row,
  Accordion,
  AccordionHeader,
  AccordionBody,
  AccordionItem,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconHelpCircle, IconSearch, IconStack2 } from "@tabler/icons-react";

type FaqItem = {
  id: string;
  title: string;
  content: string | React.ReactNode;
  isOpen?: boolean;
  icon?: React.ReactNode;
};

type AccordionSection = {
  title: string;
  items: FaqItem[];
  className?: string;
  parentId: string;
};

const FaqPage = () => {
  // FAQ data
  const faqSections: AccordionSection[] = [
    {
      title: "Frequently Asked Questions",
      parentId: "accordionExample",
      items: [
        {
          id: "collapseOne",
          title: "what is FAQs",
          isOpen: true,
          content:
            "a list of questions and answers relating to a particular subject, especially one giving basic information for users of a website. A frequently asked questions list is often used in articles, websites, email lists, and online forums where common questions tend to recur, for example through posts or queries by new users related to common knowledge gaps",
        },
        {
          id: "collapseTwo",
          title: "What is an FAQs page?",
          content:
            "Frequently Asked Questions (FAQ) pages contain a list of commonly asked questions and answers on a website about topics such as hours, shipping and handling, product information, and return policies. Sure there are chatbots, support lines, and customer reviews to help shoppers on their path to purchase, but there's one forgotten customer service tactic that is cost-effective and streamlined. That tactic is an FAQ page.",
        },
        {
          id: "collapseThree",
          title: "Why you should make an FAQs page?",
          content:
            "An FAQ page is a time-saving customer service tactic that provides the most commonly asked questions and answers for current or potential customers. Before diving into how to make an FAQ page, you need to know why having one is so important. There are so many reasons beyond improving the customer experience for perfecting your FAQ page. Keep in mind the importance of an FAQ page when developing your own e-commerce website, so you can make sure it increases sales and not the other way around.",
        },
        {
          id: "collapseFour",
          title: "How to make an FAQ page?",
          content: (
            <>
              There are seven simple steps to make the perfect FAQ page for your
              business. The design of an FAQ page is crucial for an easy-to-use
              customer experience. Follow these steps and your customer success
              team will thank you.
              <ul className="mt-3">
                <li>- Determine the questions</li>
                <li>- Categorize the questions</li>
                <li>- Highlight or link the most popular questions</li>
                <li>- Include a search bar</li>
                <li>- Align with your brand look and feel</li>
                <li>- Update regularly</li>
                <li>- Track and improve</li>
              </ul>
            </>
          ),
        },
        {
          id: "collapseFive",
          title: "What are the most frequently asked questions?",
          content: (
            <>
              A good FAQ is general enough to address many issues. There are
              questions that just about every company answers on their FAQ page,
              so if you&#39;re struggling to get started use these as your
              starting point. If they&#39;re relevant to your company, of
              course.
              <ul className="mt-3">
                <li>- What is the return policy?</li>
                <li>- What are the shipping options?</li>
                <li>- What do I do if I never received my order?</li>
                <li>- When will I receive my order?</li>
                <li>- How do I make sure I order the right size?</li>
                <li>- Where are you located?</li>
                <li>- Can I make changes to an order I already placed?</li>
                <li>- How do I get a new password?</li>
              </ul>
            </>
          ),
        },
      ],
    },
    {
      title: "Admin Dashboard",
      parentId: "nestingExample",
      className: "accordion-secondary app-accordion-plus",
      items: [
        {
          id: "nestingcollapseOne",
          title: "What is Admin Dashboard ?",
          isOpen: true,
          content:
            "The Admin Dashboard displays tabs for multiple pages that provide a personalized view of BI performance, data correctness, required cube maintenance and required administrative actions. These pages contain the results of detailed analyses, represented by links, images, graphs, pie charts and BI reports ...",
        },
        {
          id: "nestingcollapseTwo",
          title: "What is responsive Admin Dashboard",
          content:
            "An admin dashboard provides important data insights and controls for managing various aspects of an application or website. In this article, we will explore the process of creating a responsive admin dashboard using the three pillars of web development: HTML, CSS, and JavaScript.",
        },
        {
          id: "nestingcollapseThree",
          title: "Why cannot I see the dashboard in my workspace?",
          content:
            "Dashboards are not available for the customers with fiscal calendars.",
        },
        {
          id: "nestingcollapseFour",
          title: "What user role should I have to use dashboards?",
          content:
            "All workspace users can view dashboards and set up alerts on KPI changes (see Add an Alert to a KPI). Only workspace editors, explorers, and administrators can Create Dashboards.",
        },
        {
          id: "nestingcollapsefive",
          title: "Can I change the default date filter for a dashboard?",
          content:
            "Yes. Dashboards support sending regular emails with either the whole dashboard and/or individual insights from the dashboard (see Schedule Automatic Emailing of Dashboards). For each KPI on a dashboard, you can also set up email alerts to be notified when the KPI value reaches a certain threshold (see Add an Alert to a KPI).",
        },
      ],
    },
    {
      title: "Privacy & Policy",
      parentId: "accordionappflushExample",
      className: "accordion-flush accordion-light-primary",
      items: [
        {
          id: "appflush-collapseOne",
          title: "What is a Privacy Policy?",
          isOpen: true,
          content: (
            <>
              A Privacy Policy is a legal agreement designed to let visitors to
              your website or users of your app know what personal information
              you gather about them, how you use this information and how you
              keep it safe.
              <div
                className="accordion mt-2 app-accordion app-accordion-icon-left app-accordion-plus"
                id="nestingtwoExample"
              >
                {[
                  {
                    id: "nestingtwocollapseOne",
                    title: "What is privacy ?",
                    content:
                      "Privacy is the ability of an individual or group to seclude themselves or information about themselves, and thereby express themselves selectively.",
                  },
                  {
                    id: "nestingtwocollapseTwo",
                    title: "What is Policy ?",
                    content:
                      "Policy is a deliberate system of guidelines to guide decisions and achieve rational outcomes.",
                  },
                ].map((item) => (
                  <div className="accordion-item" key={item.id}>
                    <h2 className="accordion-header">
                      <button
                        className="accordion-button collapsed"
                        type="button"
                        data-bs-toggle="collapse"
                        data-bs-target={`#${item.id}`}
                        aria-expanded="false"
                      >
                        {item.title}
                      </button>
                    </h2>
                    <div
                      id={item.id}
                      className="accordion-collapse collapse"
                      data-bs-parent="#nestingtwoExample"
                    >
                      <div className="accordion-body">{item.content}</div>
                    </div>
                  </div>
                ))}
              </div>
            </>
          ),
        },
        {
          id: "appflush-collapseTwo",
          title: "what is diffusion privacy and policy",
          content:
            "The purpose of this Privacy Policy is to set out in an accountable and transparent way the collection and use of information by Diffusion. Information will only be collected for specified, explicit and legitimate purposes and not further processed in a manner that is incompatible with those purposes.",
        },
        {
          id: "appflush-collapseThree",
          title: "Why privacy policy is important",
          content:
            "The main reason to have a privacy policy is to convey to the clients that the business considers their privacy seriously. Several websites gather the client's information, and it is vital to let them know that their data is safe with the business.",
        },
      ],
    },
    {
      title: "Payment System",
      parentId: "nestingExample1",
      className:
        "accordion-flush app-accordion-plus app-accordion-icon-left accordion-light-success",
      items: [
        {
          id: "nestingcollapseplusOne1",
          title: "what is payment system",
          isOpen: true,
          content:
            "A payment system is any system used to settle financial transactions through the transfer of monetary value. This includes the institutions, payment instruments such as payment cards, people, rules, procedures, standards, and technologies that make its exchange possible.",
        },
        {
          id: "nestingcollapseplusTwo",
          title: "How many payment system types",
          content:
            "A payment can be made in the form of cash, check, wire transfer, credit card, or debit card. More modern methods of payment types leverage the Internet and digital platforms.",
        },
        {
          id: "nestingcollapseplusThree",
          title: "payment system example",
          content:
            "This includes debit cards, credit cards, electronic funds transfers, direct credits, direct debits, internet banking and e-commerce payment systems. Payment systems may be physical or electronic and each has its own procedures and protocols.",
        },
        {
          id: "nestingcollapseplusFour",
          title: "Different types of payment",
          content: (
            <ul>
              <li>- Credit Cards</li>
              <li>- Wire transfer</li>
              <li>- Debit card</li>
              <li>- Wallet</li>
              <li>- Online banking</li>
              <li>- Bank</li>
              <li>- Cash</li>
              <li>- Cheque</li>
            </ul>
          ),
        },
      ],
    },
  ];

  const [openMap, setOpenMap] = React.useState<Record<string, string>>({});

  const toggle = (sectionId: string, itemId: string) => {
    setOpenMap((prev) => ({
      ...prev,
      [sectionId]: prev[sectionId] === itemId ? "" : itemId,
    }));
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="FAQ"
          title="Apps"
          path={["FAQ"]}
          Icon={IconStack2}
        />
        <div className="faq-header text-center my-4">
          <img src="/images/logo/polytronx-dark.svg" alt="Logo" />
          <h2 className="text-dark fw-bold">How Can We Help?</h2>
          <div className="app-form search-div mt-3">
            <InputGroup className="b-r-search">
              <InputGroupText className="bg-primary text-white border-0">
                <IconSearch size={18} />
              </InputGroupText>
              <Input type="text" placeholder="Search..." />
            </InputGroup>
          </div>
        </div>

        <Row className="faq-accordion">
          {faqSections.map((section) => (
            <React.Fragment key={section.parentId}>
              <div className="faq-heading mb-3">
                <h3>
                  <IconHelpCircle size={25} className="pe-2" /> {section.title}
                </h3>
              </div>
              <div className="col-lg-8 offset-lg-2 mb-3">
                <Accordion
                  open={openMap[section.parentId] || ""}
                  toggle={(id) => toggle(section.parentId, id)}
                  className={section.className || ""}
                >
                  {section.items.map((item) => (
                    <AccordionItem key={item.id}>
                      <AccordionHeader targetId={item.id}>
                        {item.icon || (
                          <IconHelpCircle size={25} className="pe-2" />
                        )}
                        {item.title}
                      </AccordionHeader>
                      <AccordionBody accordionId={item.id}>
                        {item.content}
                      </AccordionBody>
                    </AccordionItem>
                  ))}
                </Accordion>
              </div>
            </React.Fragment>
          ))}
        </Row>
      </Container>
    </div>
  );
};

export default FaqPage;
